using Bank.Idm.Api.Security;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Endpoints;

/// <summary>
/// Boshqa filial yoki bo'limga o'tgan xodimlarning qaror kutilayotgan rollari. "mine" — men (yoki o'rinbosari bo'lgan rahbar)
/// rahbar bo'lgan xodimlar; "all" — hammasi (administrator va auditor; rahbari yo'q xodimlar faqat shu yerda).
/// Qaror huquqi MoverService'da tekshiriladi.
/// </summary>
public static class MoverEndpoints
{
    private const int MaxTake = 200;

    /// <summary>Men (yoki o'rinbosari bo'lgan rahbar) hozirgi rahbari bo'lgan xodimlar; o'zimniki emas.</summary>
    internal static IQueryable<IdentityRole> Mine(IQueryable<IdentityRole> query, Bank.Idm.Infrastructure.Requests.RequestActor actor)
    {
        var approvers = actor.Approvers;
        return query.Where(r => r.Identity.ManagerId != null && approvers.Contains(r.Identity.ManagerId.Value) && r.IdentityId != actor.IdentityId);
    }

    public static void MapMoverEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/mover-reviews", async Task<Results<Ok<PagedResult<MoverItem>>, ForbidHttpResult>> (IdmDbContext db, MoverService service,
            CurrentUser user, TimeProvider time, string? scope, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            (skip, take) = (Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
            var actor = await RequestEndpoints.ActorAsync(db, user, ct);
            var query = service.Pending(time.GetUtcNow()).AsNoTracking();
            if (scope == "all")
            {
                if (!user.IsAdmin && !user.IsAuditor) return TypedResults.Forbid();
            }
            else query = Mine(query, actor);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderBy(r => r.MoverDueAt).ThenBy(r => r.Identity.LastName).Skip(skip).Take(take)
                .Select(r => new
                {
                    r.Id, r.IdentityId, Name = r.Identity.LastName + " " + r.Identity.FirstName, r.Identity.EmployeeNumber, r.Identity.Branch,
                    r.Identity.Department, r.Identity.JobTitle, r.RoleId, r.Role.Code, RoleName = r.Role.Name, r.Role.Risk, r.Source, r.ValidTo,
                    DueAt = r.MoverDueAt!.Value, r.Identity.ManagerId,
                    ManagerName = r.Identity.Manager == null ? null : r.Identity.Manager.LastName + " " + r.Identity.Manager.FirstName
                }).ToListAsync(ct);
            var items = rows.Select(r => new MoverItem(r.Id, r.IdentityId, r.Name, r.EmployeeNumber, r.Branch, r.Department, r.JobTitle, r.RoleId, r.Code,
                r.RoleName, r.Risk, r.Source, r.ValidTo, r.DueAt, r.ManagerName,
                r.IdentityId != actor.IdentityId && (actor.IsAdmin || (r.ManagerId is { } m && actor.ActsFor(m))))).ToList();
            return TypedResults.Ok(new PagedResult<MoverItem>(items, total, skip, take));
        });

        api.MapPost("/mover-reviews/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, MoverDecisionBody body, IdmDbContext db,
            MoverService service, CurrentUser user, CancellationToken ct) =>
        {
            MoverDecision? decision = body.Decision?.ToLowerInvariant() switch { "keep" => MoverDecision.Keep, "revoke" => MoverDecision.Revoke, _ => null };
            if (decision is null) return TypedResults.Problem("Qaror: \"keep\" yoki \"revoke\"", statusCode: 400);
            var result = await service.DecideAsync(await RequestEndpoints.ActorAsync(db, user, ct), id, decision.Value, body.Comment, ct);
            return result.Value is null ? TypedResults.Problem(result.Error, statusCode: result.Status) : TypedResults.NoContent();
        });
    }
}
