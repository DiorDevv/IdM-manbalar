using System.Text.Json;
using Bank.Idm.Api.Security;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Requests;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Endpoints;

/// <summary>
/// Arizalar: har qanday xodim (portalga SSO bilan kirgan) o'zi yoki xodimlari uchun rol so'raydi va o'ziga kelgan bosqichlarni tasdiqlaydi.
/// Barcha arizalarni ko'rish — administrator va auditor. Huquq tekshiruvi AccessRequestService'da.
/// </summary>
public static class RequestEndpoints
{
    private const int MaxTake = 200;

    public static void MapRequestEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/catalog/roles", async (IdmDbContext db, string? search, CancellationToken ct) =>
        {
            var query = db.Roles.AsNoTracking().Where(r => r.IsRequestable);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = $"%{search.Trim()}%";
                query = query.Where(r => EF.Functions.ILike(r.Name, s) || EF.Functions.ILike(r.Code, s) || (r.Description != null && EF.Functions.ILike(r.Description, s)));
            }
            var roles = await query.OrderBy(r => r.Name).Take(MaxTake)
                .Select(r => new { r.Id, r.Code, r.Name, r.Description, r.Risk, Entitlements = r.Entitlements.Select(e => e.Entitlement.Application.Code + ": " + e.Entitlement.Name).ToList() })
                .ToListAsync(ct);
            return roles.Select(r => new CatalogRole(r.Id, r.Code, r.Name, r.Description, r.Risk, r.Entitlements.Order().ToList())).ToList();
        });

        api.MapGet("/me/team", async (IdmDbContext db, CurrentUser user, CancellationToken ct) =>
        {
            var me = await ResolveIdentityAsync(db, user.Login, ct);
            if (me is null) return [];
            return await db.Identities.AsNoTracking().Where(i => i.ManagerId == me && i.Status != IdentityStatus.Terminated)
                .OrderBy(i => i.LastName).ThenBy(i => i.FirstName)
                .Select(i => new PersonBrief(i.Id, i.LastName + " " + i.FirstName, i.EmployeeNumber, i.Department, i.JobTitle)).ToListAsync(ct);
        });

        api.MapPost("/requests/preview", async (PreviewRequestBody body, IdmDbContext db, AccessRequestService service, CurrentUser user, CancellationToken ct) =>
        {
            var (plan, _, error) = await service.PlanAsync(await ActorAsync(db, user, ct), body.BeneficiaryId, body.RoleId, ct);
            if (plan is null) return new RequestPreview(false, error, [], []);
            var ids = plan.Steps.Select(s => s.ApproverId).OfType<Guid>().ToList();
            var names = await db.Identities.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.LastName + " " + i.FirstName, ct);
            return new RequestPreview(true, null,
                plan.Steps.Select(s => new PreviewStep(s.Kind, s.ApproverId is { } a ? names.GetValueOrDefault(a) : null, s.Auto)).ToList(),
                plan.Conflicts.Select(c => new SodConflictInfo(c.RuleCode, c.RuleName, c.Risk.ToString(), c.LeftEntitlements, c.RightEntitlements)).ToList());
        });

        api.MapPost("/requests", async Task<Results<Created<RequestDetails>, ProblemHttpResult>> (CreateRequestBody body, IdmDbContext db,
            AccessRequestService service, CurrentUser user, CancellationToken ct) =>
        {
            var actor = await ActorAsync(db, user, ct);
            var result = await service.CreateAsync(actor, body.BeneficiaryId, body.RoleId, body.Justification, body.ValidTo, ct);
            if (result.Request is null) return TypedResults.Problem(result.Error, statusCode: result.Status);
            return TypedResults.Created($"/api/requests/{result.Request.Id}", (await LoadAsync(db, actor, result.Request.Id, ct))!);
        });

        api.MapGet("/requests", async Task<Results<Ok<PagedResult<RequestListItem>>, ForbidHttpResult>> (IdmDbContext db, CurrentUser user, string? scope,
            AccessRequestStatus? status, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            (skip, take) = (Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
            var actor = await ActorAsync(db, user, ct);
            var me = actor.IdentityId;
            var query = db.AccessRequests.AsNoTracking();
            switch (scope)
            {
                case "inbox":
                    query = Inbox(query, actor);
                    break;
                case "all":
                    if (!user.IsAdmin && !user.IsAuditor) return TypedResults.Forbid();
                    break;
                default:
                    query = query.Where(r => me != null && (r.RequesterId == me || r.BeneficiaryId == me));
                    break;
            }
            if (status is not null) query = query.Where(r => r.Status == status);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(r => r.CreatedAt).Skip(skip).Take(take)
                .Select(r => new
                {
                    r.Id, r.Status, r.RoleId, r.Role.Code, r.Role.Name, r.Role.Risk, r.BeneficiaryId,
                    Beneficiary = r.Beneficiary.LastName + " " + r.Beneficiary.FirstName, r.Beneficiary.EmployeeNumber,
                    Requester = r.Requester.LastName + " " + r.Requester.FirstName, r.CreatedAt, r.ClosedAt, HasSod = r.SodConflicts != null,
                    Current = r.Approvals.Where(a => a.Decision == ApprovalDecision.Pending).OrderBy(a => a.Step)
                        .Select(a => new { a.Kind, Approver = a.Approver == null ? null : a.Approver.LastName + " " + a.Approver.FirstName }).FirstOrDefault()
                })
                .ToListAsync(ct);
            return TypedResults.Ok(new PagedResult<RequestListItem>(items.Select(r => new RequestListItem(r.Id, r.Status, r.RoleId, r.Code, r.Name, r.Risk,
                r.BeneficiaryId, r.Beneficiary, r.EmployeeNumber, r.Requester, r.CreatedAt, r.ClosedAt, r.Current?.Kind, r.Current?.Approver, r.HasSod)).ToList(),
                total, skip, take));
        });

        api.MapGet("/requests/{id:guid}", async Task<Results<Ok<RequestDetails>, NotFound>> (Guid id, IdmDbContext db, CurrentUser user, CancellationToken ct) =>
        {
            var actor = await ActorAsync(db, user, ct);
            var details = await LoadAsync(db, actor, id, ct);
            // Ko'rinmaydigan arizaning borligi ham oshkor qilinmaydi
            if (details is null || !(user.IsAdmin || user.IsAuditor || actor.IdentityId is { } me
                    && (details.Beneficiary.Id == me || details.Requester.Id == me || details.Approvals.Any(a => a.ApproverId == me))))
                return TypedResults.NotFound();
            return TypedResults.Ok(details);
        });

        api.MapPost("/requests/{id:guid}/decision", async Task<Results<Ok<RequestDetails>, ProblemHttpResult>> (Guid id, DecisionBody body, IdmDbContext db,
            AccessRequestService service, CurrentUser user, CancellationToken ct) =>
        {
            var actor = await ActorAsync(db, user, ct);
            var result = await service.DecideAsync(actor, id, body.Approve, body.Comment, ct);
            return result.Request is null ? TypedResults.Problem(result.Error, statusCode: result.Status) : TypedResults.Ok((await LoadAsync(db, actor, id, ct))!);
        });

        api.MapPost("/requests/{id:guid}/cancel", async Task<Results<Ok<RequestDetails>, ProblemHttpResult>> (Guid id, IdmDbContext db,
            AccessRequestService service, CurrentUser user, CancellationToken ct) =>
        {
            var actor = await ActorAsync(db, user, ct);
            var result = await service.CancelAsync(actor, id, ct);
            return result.Request is null ? TypedResults.Problem(result.Error, statusCode: result.Status) : TypedResults.Ok((await LoadAsync(db, actor, id, ct))!);
        });
    }

    /// <summary>Tasdiqlash navbati: navbatdagi (eng kichik kutilayotgan) bosqich menga tegishli; o'z arizalarim kirmaydi.</summary>
    internal static IQueryable<AccessRequest> Inbox(IQueryable<AccessRequest> query, RequestActor actor)
    {
        var me = actor.IdentityId;
        var admin = actor.IsAdmin;
        return query.Where(r => r.Status == AccessRequestStatus.Pending && r.BeneficiaryId != me && r.RequesterId != me
            && r.Approvals.Any(a => a.Decision == ApprovalDecision.Pending && (a.ApproverId == me && me != null || a.ApproverId == null && admin)
                && !r.Approvals.Any(b => b.Decision == ApprovalDecision.Pending && b.Step < a.Step)));
    }

    /// <summary>Portal foydalanuvchisi → xodim: katalog (Directory) tizimidagi shu logindagi akkaunt egasi.</summary>
    internal static async Task<Guid?> ResolveIdentityAsync(IdmDbContext db, string login, CancellationToken ct) =>
        await db.Accounts.Where(a => a.Login == login && a.IdentityId != null && a.Status != AccountStatus.Deleted && a.Application.Kind == ApplicationKind.Directory)
            .OrderBy(a => a.Status).Select(a => a.IdentityId).FirstOrDefaultAsync(ct);

    internal static async Task<RequestActor> ActorAsync(IdmDbContext db, CurrentUser user, CancellationToken ct) =>
        new(user.Login, user.Name, await ResolveIdentityAsync(db, user.Login, ct), user.IsAdmin, user.IpAddress, user.CorrelationId);

    private static async Task<RequestDetails?> LoadAsync(IdmDbContext db, RequestActor actor, Guid id, CancellationToken ct)
    {
        var r = await db.AccessRequests.AsNoTracking().Include(x => x.Role).ThenInclude(x => x.Entitlements).ThenInclude(x => x.Entitlement).ThenInclude(x => x.Application)
            .Include(x => x.Beneficiary).Include(x => x.Requester).Include(x => x.Approvals).ThenInclude(x => x.Approver)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        var current = r.Approvals.Where(a => a.Decision == ApprovalDecision.Pending).OrderBy(a => a.Step).FirstOrDefault();
        var sod = r.SodConflicts is null ? [] : JsonSerializer.Deserialize<List<SodConflictInfo>>(r.SodConflicts) ?? [];
        static PersonBrief Brief(Identity i) => new(i.Id, i.LastName + " " + i.FirstName, i.EmployeeNumber, i.Department, i.JobTitle);
        return new RequestDetails(r.Id, r.Status, r.RoleId, r.Role.Code, r.Role.Name, r.Role.Description, r.Role.Risk,
            r.Role.Entitlements.Select(e => $"{e.Entitlement.Application.Code}: {e.Entitlement.Name}").Order().ToList(),
            Brief(r.Beneficiary), Brief(r.Requester), r.Justification, r.ValidTo, r.CreatedAt, r.ClosedAt,
            r.Approvals.OrderBy(a => a.Step).Select(a => new ApprovalItem(a.Step, a.Kind, a.ApproverId,
                a.Approver is null ? null : a.Approver.LastName + " " + a.Approver.FirstName, a.Decision, a.Comment, a.DecidedAt, a.DecidedBy, a.DecidedByName)).ToList(),
            sod,
            r.Status == AccessRequestStatus.Pending && current is not null && AccessRequestService.CanDecide(actor, r, current),
            r.Status == AccessRequestStatus.Pending && (r.RequesterId == actor.IdentityId || actor.IsAdmin));
    }
}
