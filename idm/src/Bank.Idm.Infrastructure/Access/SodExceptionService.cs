using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Requests;
using Bank.Idm.Infrastructure.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Access;

public record NewSodException(Guid RuleId, Guid IdentityId, string Reason, string CompensatingControl, DateOnly ValidUntil);

/// <summary>
/// SoD istisnolari: administrator beradi, asos va kompensatsion nazorat majburiy, muddat — ertadan bir yilgacha (oxirgi kun kiradi,
/// Toshkent vaqti). O'ziga istisno berib bo'lmaydi; bir qoida va xodim uchun bir vaqtda bitta amaldagi istisno. Bekor qilish — darhol.
/// Har bir amal auditda.
/// </summary>
public class SodExceptionService(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    public const int MaxDays = 366;
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly TimeZoneInfo Tashkent = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    public async Task<ReviewResult<SodException>> CreateAsync(RequestActor actor, NewSodException input, CancellationToken ct = default)
    {
        if (!actor.IsAdmin) return ReviewResult<SodException>.Fail(403, "SoD istisnosini faqat IdM administratori beradi");
        var reason = input.Reason?.Trim() ?? "";
        var control = input.CompensatingControl?.Trim() ?? "";
        if (reason.Length is < 10 or > 2000) return ReviewResult<SodException>.Fail(400, "Asos: 10–2000 belgi");
        if (control.Length is < 10 or > 2000) return ReviewResult<SodException>.Fail(400, "Kompensatsion nazorat: 10–2000 belgi");
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Tashkent).DateTime);
        if (input.ValidUntil <= today || input.ValidUntil > today.AddDays(MaxDays))
            return ReviewResult<SodException>.Fail(400, "Muddat: ertadan boshlab ko'pi bilan bir yil");
        if (actor.IdentityId == input.IdentityId) return ReviewResult<SodException>.Fail(403, "O'zingizga SoD istisnosi bera olmaysiz");

        var rule = await db.SodRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == input.RuleId, ct);
        var identity = await db.Identities.AsNoTracking().FirstOrDefaultAsync(i => i.Id == input.IdentityId, ct);
        if (rule is null || identity is null) return ReviewResult<SodException>.Fail(404, "Qoida yoki xodim topilmadi");
        if (identity.Status == IdentityStatus.Terminated) return ReviewResult<SodException>.Fail(400, "Ishdan ketgan xodimga istisno berilmaydi");
        if (await db.SodExceptions.AnyAsync(x => x.SodRuleId == rule.Id && x.IdentityId == identity.Id && x.RevokedAt == null && x.ValidTo > now, ct))
            return ReviewResult<SodException>.Fail(409, "Bu xodim uchun shu qoidada amaldagi istisno bor: avval uni bekor qiling");

        // Oxirgi kun to'liq kiradi: keyingi kun 00:00 (Toshkent) da tugaydi
        var end = input.ValidUntil.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var validTo = new DateTimeOffset(end, Tashkent.GetUtcOffset(end)).ToUniversalTime();
        var exception = new SodException
        {
            SodRuleId = rule.Id, IdentityId = identity.Id, Reason = reason, CompensatingControl = control, ValidFrom = now, ValidTo = validTo,
            CreatedAt = now, CreatedBy = actor.Login, CreatedByName = actor.Name
        };
        db.SodExceptions.Add(exception);
        await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, "sod.exception-granted", "identity", identity.Id.ToString(),
            JsonSerializer.Serialize(new { exceptionId = exception.Id, rule = rule.Code, employeeNumber = identity.EmployeeNumber, reason, compensatingControl = control,
                validUntil = input.ValidUntil.ToString("yyyy-MM-dd") }, Json), actor.IpAddress, actor.CorrelationId), ct);
        return new ReviewResult<SodException>(exception);
    }

    public async Task<ReviewResult<SodException>> RevokeAsync(RequestActor actor, Guid id, string? comment, CancellationToken ct = default)
    {
        if (!actor.IsAdmin) return ReviewResult<SodException>.Fail(403, "SoD istisnosini faqat IdM administratori bekor qiladi");
        var exception = await db.SodExceptions.Include(x => x.SodRule).Include(x => x.Identity).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (exception is null) return ReviewResult<SodException>.Fail(404, "Istisno topilmadi");
        var now = time.GetUtcNow();
        if (exception.RevokedAt is not null || exception.ValidTo <= now) return ReviewResult<SodException>.Fail(409, "Istisno allaqachon amalda emas");
        (exception.RevokedAt, exception.RevokedBy, exception.RevokedByName) = (now, actor.Login, actor.Name);
        await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, "sod.exception-revoked", "identity", exception.IdentityId.ToString(),
            JsonSerializer.Serialize(new { exceptionId = exception.Id, rule = exception.SodRule.Code, employeeNumber = exception.Identity.EmployeeNumber,
                comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()[..Math.Min(comment.Trim().Length, 2000)] }, Json), actor.IpAddress, actor.CorrelationId), ct);
        return new ReviewResult<SodException>(exception);
    }
}
