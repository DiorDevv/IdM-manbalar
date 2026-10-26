using System.Text.Json;
using Bank.Idm.Api.Security;
using Bank.Idm.Api.Sync;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Endpoints;

public static class IdmEndpoints
{
    public const string CanReadAll = "idm.read-all";     // admin yoki auditor
    public const string CanAdminister = "idm.administer"; // faqat admin
    private const int MaxTake = 200;

    public static void MapIdmEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/me", async (CurrentUser user, IdmDbContext db, CancellationToken ct) =>
        {
            var actor = await RequestEndpoints.ActorAsync(db, user, ct);
            var hasTeam = actor.IdentityId is { } me && await db.Identities.AnyAsync(i => i.ManagerId == me && i.Status != IdentityStatus.Terminated, ct);
            var inbox = await RequestEndpoints.Inbox(db.AccessRequests, actor).CountAsync(ct);
            return new MeResponse(user.Login, user.Name, user.PortalRoleClaims, actor.IdentityId, hasTeam, inbox);
        });

        api.MapGet("/dashboard", async (IdmDbContext db, ConnectorSyncCoordinator sync, Bank.Idm.Infrastructure.Access.AccessAnalyzer analyzer, CancellationToken ct) =>
        {
            var byStatus = await db.Identities.GroupBy(i => i.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            int Count(IdentityStatus s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
            var lastRuns = await ApplicationEndpoints.LastRunsAsync(db, ct);
            var lastSuccess = await db.ConnectorRuns.Where(r => r.Status == ConnectorRunStatus.Succeeded).GroupBy(r => r.ApplicationId)
                .Select(g => new { g.Key, At = g.Max(r => r.FinishedAt) }).ToDictionaryAsync(x => x.Key, x => x.At, ct);
            var connectors = (await db.Applications.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Name).Select(a => new { a.Id, a.Code, a.Name, a.Kind }).ToListAsync(ct))
                .Where(a => sync.IsConfigured(a.Code) || lastRuns.ContainsKey(a.Id))
                .Select(a => new ConnectorStatus(a.Code, a.Name, a.Kind, sync.IsRunning(a.Code), lastRuns.GetValueOrDefault(a.Id), lastSuccess.GetValueOrDefault(a.Id)))
                .ToList();
            return new DashboardSummary(
                byStatus.Sum(x => x.Count), Count(IdentityStatus.Active), Count(IdentityStatus.Suspended), Count(IdentityStatus.Terminated),
                await db.Applications.CountAsync(a => a.IsActive, ct),
                await db.Accounts.CountAsync(a => a.Status != AccountStatus.Deleted, ct),
                await db.Accounts.CountAsync(a => a.Status == AccountStatus.Disabled, ct),
                await db.Accounts.CountAsync(a => a.IdentityId == null && a.Status != AccountStatus.Deleted && a.Kind == AccountKind.Personal, ct),
                await ReportEndpoints.LeaversWithActiveAccounts(db).CountAsync(ct),
                (await analyzer.SodViolationsAsync(null, ct)).Count,
                await db.AccessRequests.CountAsync(r => r.Status == AccessRequestStatus.Pending, ct),
                await db.AuditEvents.MaxAsync(e => (DateTimeOffset?)e.OccurredAt, ct),
                connectors);
        }).RequireAuthorization(CanReadAll);

        var identities = api.MapGroup("/identities").RequireAuthorization(CanReadAll);
        identities.MapGet("/", async (IdmDbContext db, string? search, IdentityStatus? status, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            take = Math.Clamp(take, 1, MaxTake); skip = Math.Max(skip, 0);
            var query = db.Identities.AsNoTracking();
            if (status is not null) query = query.Where(i => i.Status == status);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = $"%{search.Trim()}%";
                query = query.Where(i => EF.Functions.ILike(i.LastName, s) || EF.Functions.ILike(i.FirstName, s)
                    || EF.Functions.ILike(i.EmployeeNumber, s) || (i.Email != null && EF.Functions.ILike(i.Email, s)));
            }
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(i => i.LastName).ThenBy(i => i.FirstName).Skip(skip).Take(take)
                .Select(i => new IdentityListItem(i.Id, i.EmployeeNumber,
                    i.MiddleName == null ? i.LastName + " " + i.FirstName : i.LastName + " " + i.FirstName + " " + i.MiddleName,
                    i.Email, i.Department, i.JobTitle, i.Status, i.Source, i.Accounts.Count, i.UpdatedAt))
                .ToListAsync(ct);
            return new PagedResult<IdentityListItem>(items, total, skip, take);
        });

        identities.MapGet("/{id:guid}", async Task<Results<Ok<IdentityDetails>, NotFound>> (Guid id, IdmDbContext db, CancellationToken ct) =>
        {
            var i = await db.Identities.AsNoTracking().Include(x => x.Manager).Include(x => x.Accounts).ThenInclude(a => a.Application)
                .Include(x => x.Accounts).ThenInclude(a => a.Entitlements).ThenInclude(ae => ae.Entitlement)
                .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (i is null) return TypedResults.NotFound();
            return TypedResults.Ok(new IdentityDetails(i.Id, i.EmployeeNumber, i.FirstName, i.LastName, i.MiddleName, i.Email,
                i.Department, i.JobTitle, i.ManagerId, i.Manager?.DisplayName, i.Status, i.Source, i.HireDate, i.TerminationDate,
                i.CreatedAt, i.UpdatedAt,
                i.Accounts.OrderBy(a => a.Status).ThenBy(a => a.Application.Code).Select(a => new AccountItem(a.Id, a.Application.Code, a.Application.Name,
                    a.Login, a.Status, a.Kind, a.LastSyncedAt,
                    a.Entitlements.Select(ae => ae.Entitlement).OrderByDescending(e => e.IsPrivileged).ThenBy(e => e.Name)
                        .Select(e => new EntitlementRef(e.Id, e.Name, e.IsPrivileged)).ToList())).ToList()));
        });

        // Xodim tarixi: xodimning o'zi va uning akkauntlari bo'yicha audit yozuvlari (eng yangisi birinchi)
        identities.MapGet("/{id:guid}/history", async (Guid id, IdmDbContext db, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            take = Math.Clamp(take, 1, MaxTake); skip = Math.Max(skip, 0);
            var targets = await db.Accounts.Where(a => a.IdentityId == id).Select(a => a.Id.ToString()).ToListAsync(ct);
            targets.Add(id.ToString());
            var query = db.AuditEvents.AsNoTracking().Where(e => e.TargetId != null && targets.Contains(e.TargetId) && (e.TargetType == "identity" || e.TargetType == "account"));
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(e => e.Id).Skip(skip).Take(take)
                .Select(e => new AuditItem(e.Id, e.OccurredAt, e.ActorId, e.ActorName, e.Action, e.TargetType, e.TargetId, e.Details, e.IpAddress, e.CorrelationId))
                .ToListAsync(ct);
            return new PagedResult<AuditItem>(items, total, skip, take);
        });

        identities.MapPost("/", async Task<Results<Created<IdentityDetails>, ValidationProblem, Conflict<string>>> (
            CreateIdentityRequest request, IdmDbContext db, AuditLog audit, CurrentUser user, TimeProvider time, CancellationToken ct) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
            var number = request.EmployeeNumber.Trim();
            if (await db.Identities.AnyAsync(i => i.EmployeeNumber == number, ct))
                return TypedResults.Conflict($"Tabel raqami {number} allaqachon mavjud");
            var now = time.GetUtcNow();
            var identity = new Identity
            {
                EmployeeNumber = number, FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(),
                MiddleName = Clean(request.MiddleName), Email = Clean(request.Email), Department = Clean(request.Department),
                JobTitle = Clean(request.JobTitle), HireDate = request.HireDate, Source = IdentitySource.Manual,
                CreatedAt = now, UpdatedAt = now
            };
            db.Identities.Add(identity);
            await audit.SaveChangesAsync(new AuditEntry(user.Login, user.Name, "identity.created", "identity", identity.Id.ToString(),
                JsonSerializer.Serialize(new { identity.EmployeeNumber, identity.FirstName, identity.LastName, identity.Department, identity.JobTitle }),
                user.IpAddress, user.CorrelationId), ct);
            var details = new IdentityDetails(identity.Id, identity.EmployeeNumber, identity.FirstName, identity.LastName, identity.MiddleName,
                identity.Email, identity.Department, identity.JobTitle, null, null, identity.Status, identity.Source, identity.HireDate, null,
                identity.CreatedAt, identity.UpdatedAt, []);
            return TypedResults.Created($"/api/identities/{identity.Id}", details);
        }).RequireAuthorization(CanAdminister);

        api.MapApplicationEndpoints();
        api.MapReportEndpoints();
        api.MapAccessEndpoints();
        api.MapRequestEndpoints();

        var auditGroup = api.MapGroup("/audit").RequireAuthorization(CanReadAll);
        auditGroup.MapGet("/", async (IdmDbContext db, string? action, string? actor, string? targetId, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            take = Math.Clamp(take, 1, MaxTake); skip = Math.Max(skip, 0);
            var query = db.AuditEvents.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(e => e.Action.StartsWith(action));
            if (!string.IsNullOrWhiteSpace(actor)) query = query.Where(e => e.ActorId == actor);
            if (!string.IsNullOrWhiteSpace(targetId)) query = query.Where(e => e.TargetId == targetId);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(e => e.Id).Skip(skip).Take(take)
                .Select(e => new AuditItem(e.Id, e.OccurredAt, e.ActorId, e.ActorName, e.Action, e.TargetType, e.TargetId, e.Details, e.IpAddress, e.CorrelationId))
                .ToListAsync(ct);
            return new PagedResult<AuditItem>(items, total, skip, take);
        });
        auditGroup.MapGet("/verify", async (AuditLog audit, CancellationToken ct) => await audit.VerifyChainAsync(ct));
    }

    private static Dictionary<string, string[]> Validate(CreateIdentityRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        void Require(string? value, string field, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) errors[field] = ["Majburiy maydon"];
            else if (value.Trim().Length > max) errors[field] = [$"Ko'pi bilan {max} belgi"];
        }
        Require(r.EmployeeNumber, "employeeNumber", 64);
        Require(r.FirstName, "firstName", 128);
        Require(r.LastName, "lastName", 128);
        if (r.Email is { Length: > 0 } email && (!email.Contains('@') || email.Length > 256)) errors["email"] = ["Email noto'g'ri"];
        return errors;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
