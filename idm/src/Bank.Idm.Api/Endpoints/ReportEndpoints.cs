using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Endpoints;

/// <summary>Korrelyatsiya hisobotlari va JML hodisalari lentasi (admin, auditor).</summary>
public static class ReportEndpoints
{
    private const int MaxTake = 200;

    /// <summary>Eng xavfli nomuvofiqlik: HR bo'yicha ishdan ketgan, lekin biror tizimda akkaunti hali faol.</summary>
    internal static IQueryable<Identity> LeaversWithActiveAccounts(IdmDbContext db) =>
        db.Identities.Where(i => i.Status == IdentityStatus.Terminated && i.Accounts.Any(a => a.Status == AccountStatus.Enabled));

    private static IQueryable<Identity> WithoutAccounts(IdmDbContext db) =>
        db.Identities.Where(i => i.Status == IdentityStatus.Active && !i.Accounts.Any(a => a.Status != AccountStatus.Deleted));

    private static IQueryable<Identity> NotInHr(IdmDbContext db) =>
        db.Identities.Where(i => i.Source != IdentitySource.Hr && i.Status != IdentityStatus.Terminated);

    private static IQueryable<Account> Orphans(IdmDbContext db) =>
        db.Accounts.Where(a => a.IdentityId == null && a.Status != AccountStatus.Deleted && a.Kind == AccountKind.Personal);

    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/reports").RequireAuthorization(IdmEndpoints.CanReadAll);

        reports.MapGet("/correlation", async (IdmDbContext db, CancellationToken ct) => new CorrelationSummary(
            await Orphans(db).CountAsync(ct), await LeaversWithActiveAccounts(db).CountAsync(ct), await WithoutAccounts(db).CountAsync(ct),
            await NotInHr(db).CountAsync(ct),
            await db.Applications.AnyAsync(a => a.Kind == ApplicationKind.HumanResources && a.IsActive, ct)));

        reports.MapGet("/orphan-accounts", async (IdmDbContext db, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            (skip, take) = Page(skip, take);
            var query = Orphans(db).AsNoTracking();
            var items = await query.OrderBy(a => a.Application.Code).ThenBy(a => a.Login).Skip(skip).Take(take)
                .Select(a => new OrphanAccountItem(a.Id, a.Application.Code, a.Application.Name, a.Login, a.DisplayName, a.EmployeeNumber, a.Status, a.LastSyncedAt))
                .ToListAsync(ct);
            return new PagedResult<OrphanAccountItem>(items, await query.CountAsync(ct), skip, take);
        });

        reports.MapGet("/leavers-with-active-accounts", async (IdmDbContext db, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            (skip, take) = Page(skip, take);
            var query = LeaversWithActiveAccounts(db).AsNoTracking();
            var items = await query.OrderBy(i => i.TerminationDate).ThenBy(i => i.LastName).Skip(skip).Take(take)
                .Select(i => new LeaverItem(i.Id, i.EmployeeNumber, i.LastName + " " + i.FirstName, i.Department, i.TerminationDate,
                    i.Accounts.Where(a => a.Status == AccountStatus.Enabled).OrderBy(a => a.Application.Code)
                        .Select(a => new AccountItem(a.Id, a.Application.Code, a.Application.Name, a.Login, a.Status, a.Kind, a.LastSyncedAt, new List<EntitlementRef>()))
                        .ToList()))
                .ToListAsync(ct);
            return new PagedResult<LeaverItem>(items, await query.CountAsync(ct), skip, take);
        });

        reports.MapGet("/identities-without-accounts", (IdmDbContext db, int skip = 0, int take = 50, CancellationToken ct = default) =>
            IdentityPage(WithoutAccounts(db), skip, take, ct));

        reports.MapGet("/not-in-hr", (IdmDbContext db, int skip = 0, int take = 50, CancellationToken ct = default) =>
            IdentityPage(NotInHr(db), skip, take, ct));

        api.MapGet("/events", async (IdmDbContext db, IdentityEventKind? kind, Guid? identityId, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            (skip, take) = Page(skip, take);
            var query = db.IdentityEvents.AsNoTracking();
            if (kind is not null) query = query.Where(e => e.Kind == kind);
            if (identityId is not null) query = query.Where(e => e.IdentityId == identityId);
            var items = await query.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Skip(skip).Take(take)
                .Select(e => new IdentityEventItem(e.Id, e.Kind, e.OccurredAt, e.Source, e.IdentityId, e.Identity.LastName + " " + e.Identity.FirstName,
                    e.Identity.EmployeeNumber, e.Identity.Department, e.Details))
                .ToListAsync(ct);
            return new PagedResult<IdentityEventItem>(items, await query.CountAsync(ct), skip, take);
        }).RequireAuthorization(IdmEndpoints.CanReadAll);
    }

    private static async Task<PagedResult<IdentityListItem>> IdentityPage(IQueryable<Identity> source, int skip, int take, CancellationToken ct)
    {
        (skip, take) = Page(skip, take);
        var query = source.AsNoTracking();
        var items = await query.OrderBy(i => i.LastName).ThenBy(i => i.FirstName).Skip(skip).Take(take)
            .Select(i => new IdentityListItem(i.Id, i.EmployeeNumber,
                i.MiddleName == null ? i.LastName + " " + i.FirstName : i.LastName + " " + i.FirstName + " " + i.MiddleName,
                i.Email, i.Department, i.JobTitle, i.Status, i.Source, i.Accounts.Count(a => a.Status != AccountStatus.Deleted), i.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResult<IdentityListItem>(items, await query.CountAsync(ct), skip, take);
    }

    private static (int Skip, int Take) Page(int skip, int take) => (Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
}
