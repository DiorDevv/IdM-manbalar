using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Requests;
using Bank.Idm.Infrastructure.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Access;

public enum MoverDecision
{
    /// <summary>Rol yangi joyda ham kerak: qaror muddati olib tashlanadi, rol davom etadi.</summary>
    Keep,
    /// <summary>Rol endi kerak emas: darhol tugaydi (tizimdan olib tashlash — provisioning orqali).</summary>
    Revoke
}

/// <summary>
/// Boshqa filial yoki bo'limga o'tgan xodimning ariza/qo'lda berilgan rollari bo'yicha qaror (HR importi
/// <see cref="IdentityRole.MoverDueAt"/> qo'yadi). Qaror qiladi: xodimning hozirgi (yangi) rahbari, uning amaldagi o'rinbosari
/// yoki IdM administratori; xodim o'z roli haqida qaror qila olmaydi. Muddat o'tgach qaror qilinmagan rollar tugaydi
/// (<see cref="ExpireAsync"/>, faqat yetakchi serverda). Har bir o'zgarish shartli yangilash bilan: qaror va muddat tugashi
/// bir vaqtda kelsa, faqat bittasi qo'llanadi.
/// </summary>
public class MoverService(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    public const int MaxCommentLength = 500;
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Qaror kutilayotgan (muddati hali o'tmagan yoki fon ishi hali tugatmagan) amaldagi rollar.</summary>
    public IQueryable<IdentityRole> Pending(DateTimeOffset now) =>
        db.IdentityRoles.Where(r => r.MoverDueAt != null && (r.ValidTo == null || r.ValidTo > now));

    public async Task<ReviewResult<IdentityRole>> DecideAsync(RequestActor actor, Guid assignmentId, MoverDecision decision, string? comment,
        CancellationToken ct = default)
    {
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (text?.Length > MaxCommentLength) return ReviewResult<IdentityRole>.Fail(400, $"Izoh ko'pi bilan {MaxCommentLength} belgi");
        // Rolni qoldirish — asos yozilishi shart (attestatsiya va audit uchun dalil)
        if (decision == MoverDecision.Keep && (text is null || text.Length < 5))
            return ReviewResult<IdentityRole>.Fail(400, "Rolni qoldirish sababini yozing (kamida 5 belgi)");

        var now = time.GetUtcNow();
        var a = await db.IdentityRoles.AsNoTracking().Include(r => r.Role).Include(r => r.Identity)
            .FirstOrDefaultAsync(r => r.Id == assignmentId, ct);
        if (a is null) return ReviewResult<IdentityRole>.Fail(404, "Rol topilmadi");
        if (a.MoverDueAt is null || (a.ValidTo is { } end && end <= now))
            return ReviewResult<IdentityRole>.Fail(409, "Bu rol bo'yicha qaror kutilmayapti (allaqachon hal qilingan yoki tugagan)");
        if (a.IdentityId == actor.IdentityId)
            return ReviewResult<IdentityRole>.Fail(403, "O'z rolingiz haqida qaror qila olmaysiz");
        var manager = a.Identity.ManagerId;
        if (!actor.IsAdmin && (manager is null || !actor.ActsFor(manager.Value)))
            return ReviewResult<IdentityRole>.Fail(403, "Qarorni xodimning rahbari (yoki o'rinbosari) yoki administrator qiladi");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Shartli yangilash: muddat shu orada tugagan yoki boshqa kishi qaror qilgan bo'lsa — hech narsa o'zgarmaydi
        var query = db.IdentityRoles.Where(r => r.Id == assignmentId && r.MoverDueAt != null && (r.ValidTo == null || r.ValidTo > now));
        var changed = decision == MoverDecision.Keep
            ? await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.MoverDueAt, (DateTimeOffset?)null), ct)
            : await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.MoverDueAt, (DateTimeOffset?)null).SetProperty(r => r.ValidTo, now), ct);
        if (changed == 0) return ReviewResult<IdentityRole>.Fail(409, "Bu rol bo'yicha qaror allaqachon qilingan");

        var onBehalfOf = actor.OnBehalfOf(manager);
        var details = new
        {
            role = a.Role.Code, employeeNumber = a.Identity.EmployeeNumber, source = a.Source.ToString().ToLowerInvariant(),
            comment = text, onBehalfOf, reason = decision == MoverDecision.Revoke ? "mover-revoked" : null
        };
        await audit.SaveChangesAsync([new AuditEntry(actor.Login, actor.Name,
            decision == MoverDecision.Keep ? "identity.role-mover-kept" : "identity.role-removed", "identity", a.IdentityId.ToString(),
            JsonSerializer.Serialize(details, Json), actor.IpAddress, actor.CorrelationId)], ct);
        await transaction.CommitAsync(ct);
        return new ReviewResult<IdentityRole>(a);
    }

    /// <summary>Muddati o'tgan, qaror qilinmagan rollarni tugatadi. Qaytaradi: tugatilganlar soni.</summary>
    public async Task<int> ExpireAsync(string actorId, string? actorName, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var due = await db.IdentityRoles.AsNoTracking().Where(r => r.MoverDueAt <= now && (r.ValidTo == null || r.ValidTo > now))
            .OrderBy(r => r.MoverDueAt).Take(500)
            .Select(r => new { r.Id, r.IdentityId, Role = r.Role.Code, r.Identity.EmployeeNumber, r.Source, r.MoverDueAt }).ToListAsync(ct);
        var expired = 0;
        foreach (var a in due)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var changed = await db.IdentityRoles.Where(r => r.Id == a.Id && r.MoverDueAt <= now && (r.ValidTo == null || r.ValidTo > now))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.MoverDueAt, (DateTimeOffset?)null).SetProperty(r => r.ValidTo, now), ct);
            if (changed == 0) continue;
            await audit.SaveChangesAsync([new AuditEntry(actorId, actorName, "identity.role-removed", "identity", a.IdentityId.ToString(),
                JsonSerializer.Serialize(new { role = a.Role, employeeNumber = a.EmployeeNumber, source = a.Source.ToString().ToLowerInvariant(),
                    reason = "mover-grace-expired", dueAt = a.MoverDueAt }, Json))], ct);
            await transaction.CommitAsync(ct);
            expired++;
        }
        return expired;
    }
}
