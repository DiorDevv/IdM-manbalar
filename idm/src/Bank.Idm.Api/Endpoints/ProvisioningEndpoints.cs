using Bank.Idm.Api.Security;
using Bank.Idm.Api.Sync;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Provisioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Endpoints;

/// <summary>
/// Tizimga yozish: amallar navbati (ko'rish — administrator va auditor), dry-run reja, tasdiqlash, qayta urinish, yopish (faqat administrator).
/// </summary>
public static class ProvisioningEndpoints
{
    private const int MaxTake = 200;

    public static void MapProvisioningEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/provisioning").RequireAuthorization(IdmEndpoints.CanReadAll);

        group.MapGet("/status", async (IdmDbContext db, ProvisioningCoordinator provisioning, TimeProvider time, CancellationToken ct) =>
        {
            var since = time.GetUtcNow().AddDays(-1);
            var counts = await db.ProvisioningOperations.Where(o => o.Status != ProvisioningStatus.Succeeded || o.CompletedAt >= since)
                .Where(o => o.Status != ProvisioningStatus.Obsolete && o.Status != ProvisioningStatus.Cancelled)
                .GroupBy(o => new { o.ApplicationId, o.Status }).Select(g => new { g.Key.ApplicationId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
            var enabled = provisioning.Enabled;
            var apps = await db.Applications.AsNoTracking().Where(a => a.Kind != ApplicationKind.HumanResources).OrderBy(a => a.Name)
                .Select(a => new { a.Id, a.Code, a.Name }).ToListAsync(ct);
            int Count(Guid app, ProvisioningStatus s) => counts.FirstOrDefault(c => c.ApplicationId == app && c.Status == s)?.Count ?? 0;
            return apps.Where(a => enabled.ContainsKey(a.Code) || counts.Any(c => c.ApplicationId == a.Id))
                .Select(a =>
                {
                    var d = enabled.GetValueOrDefault(a.Code);
                    return new ProvisioningApplicationStatus(a.Code, a.Name, provisioning.ModeOf(a.Code), d?.Provisioning.CreateAccounts ?? false,
                        d?.Provisioning.MaxAutoOperations ?? 0, Count(a.Id, ProvisioningStatus.Proposed), Count(a.Id, ProvisioningStatus.Queued),
                        Count(a.Id, ProvisioningStatus.Running), Count(a.Id, ProvisioningStatus.Failed), Count(a.Id, ProvisioningStatus.Succeeded));
                }).ToList();
        });

        group.MapGet("/operations", async (IdmDbContext db, [Microsoft.AspNetCore.Mvc.FromQuery] ProvisioningStatus[]? status, string? application,
            string? search, int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            (skip, take) = (Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
            var query = db.ProvisioningOperations.AsNoTracking();
            if (status is { Length: > 0 }) query = query.Where(o => status.Contains(o.Status));
            if (!string.IsNullOrWhiteSpace(application)) query = query.Where(o => o.Application.Code == application);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = $"%{search.Trim()}%";
                query = query.Where(o => (o.Account != null && EF.Functions.ILike(o.Account.Login, s))
                    || (o.Identity != null && (EF.Functions.ILike(o.Identity.LastName, s) || EF.Functions.ILike(o.Identity.FirstName, s) || EF.Functions.ILike(o.Identity.EmployeeNumber, s)))
                    || (o.Entitlement != null && EF.Functions.ILike(o.Entitlement.Name, s)));
            }
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Kind).Skip(skip).Take(take)
                .Select(o => new ProvisioningOperationItem(o.Id, o.Application.Code, o.Kind, o.Status, o.Reason, o.IdentityId,
                    o.Identity == null ? null : o.Identity.LastName + " " + o.Identity.FirstName, o.Identity == null ? null : o.Identity.EmployeeNumber,
                    o.AccountId, o.Account == null ? null : o.Account.Login, o.Entitlement == null ? null : o.Entitlement.Name,
                    o.Entitlement != null && o.Entitlement.IsPrivileged, o.Summary, o.CreatedAt, o.ApprovedBy, o.ApprovedByName, o.ApprovedAt,
                    o.Attempts, o.NextAttemptAt, o.LastError, o.CompletedAt))
                .ToListAsync(ct);
            return new PagedResult<ProvisioningOperationItem>(items, total, skip, take);
        });

        group.MapPost("/{code}/plan", async Task<Results<Ok<PlanResult>, NotFound>> (string code, ProvisioningCoordinator provisioning, CurrentUser user,
            CancellationToken ct) =>
        {
            var result = await provisioning.PlanAsync(code, user.Login, user.Name, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }).RequireAuthorization(IdmEndpoints.CanAdminister);

        group.MapPost("/operations/{action}", async Task<Results<Ok<ProvisioningActionResult>, ProblemHttpResult>> (string action, ProvisioningActionBody body,
            IdmDbContext db, ProvisioningService service, ProvisioningCoordinator provisioning, CurrentUser user, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ProvisioningAction>(action, ignoreCase: true, out var kind) || int.TryParse(action, out _))
                return TypedResults.Problem("Noma'lum amal: approve, retry yoki dismiss", statusCode: 400);
            var codes = provisioning.Enabled.Keys.ToList();
            var enabled = (await db.Applications.Where(a => codes.Contains(a.Code)).Select(a => a.Id).ToListAsync(ct)).ToHashSet();
            var result = await service.ApplyAsync(kind, body.Ids ?? [], enabled, user.Login, user.Name, user.IpAddress, user.CorrelationId, ct);
            return result.Error is not null && result.Changed == 0
                ? TypedResults.Problem(result.Error, statusCode: result.Error.Contains("o'zgartirildi") ? 409 : 400)
                : TypedResults.Ok(result);
        }).RequireAuthorization(IdmEndpoints.CanAdminister);
    }
}
