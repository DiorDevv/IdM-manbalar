using System.Linq.Expressions;
using Bank.Idm.Api.Security;
using Bank.Idm.Api.Sync;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Endpoints;

/// <summary>Tizimlar: ro'yxat, kartochka (akkauntlar, guruhlar, sinxronlash tarixi) va qo'lda sinxronlash.</summary>
public static class ApplicationEndpoints
{
    private const int MaxTake = 200;

    internal static readonly Expression<Func<ConnectorRun, ConnectorRunItem>> ToRunItem = r => new ConnectorRunItem(r.Id, r.Trigger, r.TriggeredBy,
        r.Status, r.StartedAt, r.FinishedAt, r.AccountsRead, r.GroupsRead, r.AccountsCreated, r.AccountsUpdated, r.AccountsDisabled,
        r.AccountsDeleted, r.MembershipsAdded, r.MembershipsRemoved, r.IdentitiesCreated, r.IdentitiesUpdated, r.IdentitiesTerminated,
        r.IdentitiesMissing, r.OrphanAccounts, r.Error);
    private static readonly Func<ConnectorRun, ConnectorRunItem> ToRunItemCompiled = ToRunItem.Compile();

    public static void MapApplicationEndpoints(this RouteGroupBuilder api)
    {
        var apps = api.MapGroup("/applications").RequireAuthorization(IdmEndpoints.CanReadAll);

        apps.MapGet("/", async (IdmDbContext db, ConnectorSyncCoordinator sync, CancellationToken ct) =>
        {
            var items = await db.Applications.AsNoTracking().OrderBy(a => a.Name)
                .Select(a => new
                {
                    a.Id, a.Code, a.Name, a.Description, a.Kind, a.Risk, a.IsActive,
                    Accounts = a.Accounts.Count(x => x.Status != AccountStatus.Deleted),
                    Entitlements = a.Entitlements.Count(e => e.DeletedAt == null)
                }).ToListAsync(ct);
            var lastRuns = await LastRunsAsync(db, ct);
            return items.Select(a => new ApplicationItem(a.Id, a.Code, a.Name, a.Description, a.Kind, a.Risk, a.IsActive, a.Accounts, a.Entitlements,
                sync.IsConfigured(a.Code), sync.IsRunning(a.Code), lastRuns.GetValueOrDefault(a.Id))).ToList();
        });

        apps.MapGet("/{code}", async Task<Results<Ok<ApplicationDetails>, NotFound>> (string code, IdmDbContext db, ConnectorSyncCoordinator sync,
            IOptionsMonitor<IdmOptions> options, CancellationToken ct) =>
        {
            var a = await db.Applications.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
            if (a is null) return TypedResults.NotFound();
            var byStatus = await db.Accounts.Where(x => x.ApplicationId == a.Id).GroupBy(x => x.Status)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var orphans = await db.Accounts.CountAsync(x => x.ApplicationId == a.Id && x.Status != AccountStatus.Deleted && x.IdentityId == null && x.Kind == AccountKind.Personal, ct);
            var entitlements = await db.Entitlements.Where(e => e.ApplicationId == a.Id && e.DeletedAt == null)
                .GroupBy(e => e.IsPrivileged).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var runs = db.ConnectorRuns.AsNoTracking().Where(r => r.ApplicationId == a.Id);
            var lastRun = await runs.OrderByDescending(r => r.StartedAt).Select(ToRunItem).FirstOrDefaultAsync(ct);
            var lastSuccess = await runs.Where(r => r.Status == ConnectorRunStatus.Succeeded).MaxAsync(r => r.FinishedAt, ct);
            options.CurrentValue.Connectors.TryGetValue(a.Code, out var definition);
            return TypedResults.Ok(new ApplicationDetails(a.Id, a.Code, a.Name, a.Description, a.Kind, a.Risk, a.IsActive,
                byStatus.GetValueOrDefault(AccountStatus.Enabled), byStatus.GetValueOrDefault(AccountStatus.Disabled), byStatus.GetValueOrDefault(AccountStatus.Deleted),
                orphans, entitlements.Values.Sum(), entitlements.GetValueOrDefault(true),
                sync.IsConfigured(a.Code), sync.IsRunning(a.Code), definition is { Enabled: true } ? definition.IntervalMinutes : null, lastRun, lastSuccess));
        });

        apps.MapGet("/{code}/accounts", async Task<Results<Ok<PagedResult<ApplicationAccountItem>>, NotFound>> (string code, IdmDbContext db,
            string? search, AccountStatus? status, bool? orphan, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            var appId = await db.Applications.Where(a => a.Code == code).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (appId is null) return TypedResults.NotFound();
            take = Math.Clamp(take, 1, MaxTake); skip = Math.Max(skip, 0);
            var query = db.Accounts.AsNoTracking().Where(a => a.ApplicationId == appId);
            query = status is null ? query.Where(a => a.Status != AccountStatus.Deleted) : query.Where(a => a.Status == status);
            if (orphan == true) query = query.Where(a => a.IdentityId == null && a.Kind == AccountKind.Personal);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = $"%{search.Trim()}%";
                query = query.Where(a => EF.Functions.ILike(a.Login, s) || (a.DisplayName != null && EF.Functions.ILike(a.DisplayName, s))
                    || (a.EmployeeNumber != null && EF.Functions.ILike(a.EmployeeNumber, s)) || (a.Email != null && EF.Functions.ILike(a.Email, s)));
            }
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(a => a.Login).Skip(skip).Take(take)
                .Select(a => new ApplicationAccountItem(a.Id, a.Login, a.DisplayName, a.Email, a.EmployeeNumber, a.Status, a.Kind, a.IdentityId,
                    a.Identity == null ? null : a.Identity.LastName + " " + a.Identity.FirstName, a.Entitlements.Count, a.LastSyncedAt))
                .ToListAsync(ct);
            return TypedResults.Ok(new PagedResult<ApplicationAccountItem>(items, total, skip, take));
        });

        apps.MapGet("/{code}/entitlements", async Task<Results<Ok<PagedResult<EntitlementItem>>, NotFound>> (string code, IdmDbContext db,
            string? search, bool includeDeleted = false, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            var appId = await db.Applications.Where(a => a.Code == code).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (appId is null) return TypedResults.NotFound();
            take = Math.Clamp(take, 1, MaxTake); skip = Math.Max(skip, 0);
            var query = db.Entitlements.AsNoTracking().Where(e => e.ApplicationId == appId);
            if (!includeDeleted) query = query.Where(e => e.DeletedAt == null);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(e => EF.Functions.ILike(e.Name, $"%{search.Trim()}%"));
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(e => e.IsPrivileged).ThenBy(e => e.Name).Skip(skip).Take(take)
                .Select(e => new EntitlementItem(e.Id, e.Name, e.Description, e.Risk, e.IsPrivileged,
                    db.AccountEntitlements.Count(ae => ae.EntitlementId == e.Id && ae.Account.Status != AccountStatus.Deleted), e.DeletedAt, e.LastSyncedAt))
                .ToListAsync(ct);
            return TypedResults.Ok(new PagedResult<EntitlementItem>(items, total, skip, take));
        });

        apps.MapGet("/{code}/runs", async Task<Results<Ok<PagedResult<ConnectorRunItem>>, NotFound>> (string code, IdmDbContext db,
            int skip = 0, int take = 20, CancellationToken ct = default) =>
        {
            var appId = await db.Applications.Where(a => a.Code == code).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (appId is null) return TypedResults.NotFound();
            take = Math.Clamp(take, 1, MaxTake); skip = Math.Max(skip, 0);
            var query = db.ConnectorRuns.AsNoTracking().Where(r => r.ApplicationId == appId);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(r => r.StartedAt).Skip(skip).Take(take).Select(ToRunItem).ToListAsync(ct);
            return TypedResults.Ok(new PagedResult<ConnectorRunItem>(items, total, skip, take));
        });

        // Qo'lda sinxronlash: fonda boshlanadi, natija /runs da. allowMassDelete — xavfsizlik chegarasini administrator tasdiqlagan
        apps.MapPost("/{code}/sync", async Task<Results<Accepted<SyncStarted>, NotFound, ProblemHttpResult>> (string code, bool? allowMassDelete,
            IdmDbContext db, ConnectorSyncCoordinator sync, AuditLog audit, CurrentUser user, CancellationToken ct) =>
        {
            var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code, ct);
            if (app is null) return TypedResults.NotFound();
            var started = await sync.StartAsync(app.Code, ConnectorRunTrigger.Manual, user.Login, user.Name, allowMassDelete == true);
            switch (started.Status)
            {
                case SyncStartStatus.NotConfigured:
                    return TypedResults.Problem($"{app.Code} uchun konnektor sozlanmagan", statusCode: StatusCodes.Status400BadRequest);
                case SyncStartStatus.AlreadyRunning:
                    return TypedResults.Problem($"{app.Code} sinxronlanmoqda, tugashini kuting", statusCode: StatusCodes.Status409Conflict);
            }
            if (allowMassDelete == true)
                await audit.SaveChangesAsync(new AuditEntry(user.Login, user.Name, "connector.mass-delete-approved", "application", app.Id.ToString(),
                    $$"""{"application":"{{app.Code}}","runId":"{{started.RunId}}"}""", user.IpAddress, user.CorrelationId), ct);
            return TypedResults.Accepted($"/api/applications/{app.Code}/runs", new SyncStarted(started.RunId!.Value));
        }).RequireAuthorization(IdmEndpoints.CanAdminister);
    }

    internal static async Task<Dictionary<Guid, ConnectorRunItem>> LastRunsAsync(IdmDbContext db, CancellationToken ct)
    {
        var ids = await db.ConnectorRuns.GroupBy(r => r.ApplicationId).Select(g => g.OrderByDescending(r => r.StartedAt).Select(r => r.Id).First()).ToListAsync(ct);
        return await db.ConnectorRuns.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.ApplicationId, ToRunItemCompiled, ct);
    }
}
