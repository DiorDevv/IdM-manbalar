using Bank.Idm.Api.Security;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Endpoints;

/// <summary>
/// Ikki kishilik tasdiq so'rovlari va portal vakolatlari. Ko'rish — administrator va auditor; tasdiqlash, rad etish va vakolat
/// berish/olish — administrator (vakolat o'zgarishi ham ikki kishilik tasdiq bilan). Qoidalar AdminChanges'da.
/// </summary>
public static class ChangeRequestEndpoints
{
    private const int MaxTake = 200;

    public static void MapChangeRequestEndpoints(this RouteGroupBuilder api)
    {
        var read = api.MapGroup("").RequireAuthorization(IdmEndpoints.CanReadAll);
        var admin = api.MapGroup("").RequireAuthorization(IdmEndpoints.CanAdminister);

        read.MapGet("/change-requests", async (IdmDbContext db, CurrentUser user, TimeProvider time, string? status, int skip = 0, int take = 50,
            CancellationToken ct = default) =>
        {
            (skip, take) = (Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
            var now = time.GetUtcNow();
            var query = db.ChangeRequests.AsNoTracking();
            if (status == "pending") query = query.Where(r => r.Status == ChangeRequestStatus.Pending && r.ExpiresAt > now);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(r => r.RequestedAt).Skip(skip).Take(take).ToListAsync(ct);
            return new PagedResult<ChangeRequestItem>(items.Select(r => AdminChanges.ToItem(r, now, user.Login, user.IsAdmin)).ToList(), total, skip, take);
        });

        admin.MapPost("/change-requests/{id:guid}/approve", (Guid id, ChangeDecisionBody body, AdminChanges changes, CancellationToken ct) =>
            changes.ApproveAsync(id, body.Comment, ct));
        admin.MapPost("/change-requests/{id:guid}/reject", (Guid id, ChangeDecisionBody body, AdminChanges changes, CancellationToken ct) =>
            changes.RejectAsync(id, body.Comment, ct));
        admin.MapPost("/change-requests/{id:guid}/cancel", (Guid id, AdminChanges changes, CancellationToken ct) => changes.CancelAsync(id, ct));

        // ---------- Portal vakolatlari (administrator, auditor, HR) ----------
        read.MapGet("/portal-roles", async (IdmDbContext db, CancellationToken ct) =>
        {
            var items = await db.PortalRoleAssignments.AsNoTracking().OrderBy(p => p.Role).ThenBy(p => p.Login).ToListAsync(ct);
            var logins = items.Select(p => p.Login).Distinct().ToList();
            // Loginni xodimga katalog (AD) akkaunti orqali bog'laymiz — ism ko'rinishi uchun
            var names = (await db.Accounts.AsNoTracking()
                    .Where(a => logins.Contains(a.Login) && a.IdentityId != null && a.Status != AccountStatus.Deleted && a.Application.Kind == ApplicationKind.Directory)
                    .Select(a => new { a.Login, Name = a.Identity!.LastName + " " + a.Identity.FirstName }).ToListAsync(ct))
                .GroupBy(a => a.Login).ToDictionary(g => g.Key, g => g.First().Name);
            return items.Select(p => new PortalRoleItem(p.Id, p.Login, p.Role, names.GetValueOrDefault(p.Login), p.CreatedAt, p.CreatedBy)).ToList();
        });

        admin.MapPost("/portal-roles", (PortalRoleBody body, AdminChanges changes, CancellationToken ct) =>
            changes.SubmitAsync(ChangeKinds.PortalRoleGrant, new PortalRoleChange(body.Login, body.Role), ct));
        admin.MapPost("/portal-roles/revoke", (PortalRoleBody body, AdminChanges changes, CancellationToken ct) =>
            changes.SubmitAsync(ChangeKinds.PortalRoleRevoke, new PortalRoleChange(body.Login, body.Role), ct));
    }
}
