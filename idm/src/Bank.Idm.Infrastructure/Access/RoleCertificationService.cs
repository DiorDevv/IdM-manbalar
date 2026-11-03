using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Requests;
using Bank.Idm.Infrastructure.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Access;

/// <summary>
/// Rol tarkibi attestatsiyasi: rol egasi (yoki uning o'rinbosari; egasi yo'q bo'lsa — administrator) rol ruxsatlari hamon kerakligini
/// tasdiqlaydi va keraksizlarini olib tashlaydi. Ega faqat olib tashlay oladi (qo'shish — administrator rol tahririda), olib
/// tashlashda izoh majburiy. Olib tashlangan ruxsat rolning barcha xodimlaridan tizimga yozish orqali olinadi (IdM qo'shgan a'zoliklar).
/// </summary>
public class RoleCertificationService(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static bool CanCertify(RequestActor actor, Role role) =>
        role.OwnerId is { } owner ? actor.ActsFor(owner) : actor.IsAdmin;

    public async Task<ReviewResult<Role>> CertifyAsync(RequestActor actor, Guid roleId, IReadOnlyCollection<Guid> keep, string? comment, CancellationToken ct = default)
    {
        var role = await db.Roles.Include(r => r.Entitlements).ThenInclude(e => e.Entitlement).ThenInclude(e => e.Application)
            .FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null || !CanCertify(actor, role) && !actor.IsAdmin) return ReviewResult<Role>.Fail(404, "Rol topilmadi");
        if (!CanCertify(actor, role)) return ReviewResult<Role>.Fail(403, "Rol tarkibini uning egasi tasdiqlaydi");
        var current = role.Entitlements.Select(e => e.EntitlementId).ToHashSet();
        if (keep.Any(k => !current.Contains(k))) return ReviewResult<Role>.Fail(400, "Faqat roldagi ruxsatlarni qoldirish mumkin (qo'shish — administrator)");
        var removed = role.Entitlements.Where(e => !keep.Contains(e.EntitlementId)).ToList();
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (text?.Length > 2000) return ReviewResult<Role>.Fail(400, "Izoh ko'pi bilan 2000 belgi");
        if (removed.Count > 0 && (text is null || text.Length < 3)) return ReviewResult<Role>.Fail(400, "Olib tashlash sababini yozing");

        var now = time.GetUtcNow();
        foreach (var e in removed) role.Entitlements.Remove(e);
        (role.CertifiedAt, role.CertifiedBy, role.CertifiedByName) = (now, actor.Login, actor.Name);
        await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, "role.certified", "role", role.Id.ToString(), JsonSerializer.Serialize(new
        {
            role.Code, kept = keep.Count, removed = removed.Select(e => $"{e.Entitlement.Application.Code}: {e.Entitlement.Name}"), comment = text,
            onBehalfOf = actor.OnBehalfOf(role.OwnerId)
        }, Json), actor.IpAddress, actor.CorrelationId), ct);
        return new(role);
    }
}
