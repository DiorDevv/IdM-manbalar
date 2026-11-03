using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Requests;

/// <param name="DelegatorId">Bo'sh — o'zim uchun; boshqa xodim uchun faqat administrator.</param>
public record NewDelegation(Guid? DelegatorId, Guid DelegateId, DateOnly From, DateOnly Until, string? Reason);

/// <summary>
/// O'rinbosarlik: xodim o'zi (yoki administrator istalgan xodim uchun) muddatli o'rinbosar tayinlaydi. Muddat — bugundan, ko'pi bilan
/// 180 kun (Toshkent vaqti, kunlar to'liq). Bir xodimda bir vaqtda bitta o'rinbosarlik; o'rinbosar — ishlayotgan boshqa xodim.
/// Bekor qilish — tayinlagan xodim yoki administrator.
/// </summary>
public class DelegationService(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    public const int MaxDays = 180;
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly TimeZoneInfo Tashkent = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    /// <summary>Hozir shu xodim kimlarning o'rinbosari (ishdan ketgan xodim o'rniga qaror qilinmaydi).</summary>
    public static async Task<IReadOnlyList<Guid>> ActingForAsync(IdmDbContext db, Guid delegateId, DateTimeOffset now, CancellationToken ct) =>
        await db.ApproverDelegations.AsNoTracking()
            .Where(d => d.DelegateId == delegateId && d.RevokedAt == null && d.ValidFrom <= now && d.ValidTo > now && d.Delegator.Status != IdentityStatus.Terminated)
            .Select(d => d.DelegatorId).Distinct().ToListAsync(ct);

    private static DateTimeOffset StartOf(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, Tashkent.GetUtcOffset(local)).ToUniversalTime();
    }

    public async Task<ReviewResult<ApproverDelegation>> CreateAsync(RequestActor actor, NewDelegation input, CancellationToken ct = default)
    {
        var delegatorId = input.DelegatorId ?? actor.IdentityId;
        if (delegatorId is null) return ReviewResult<ApproverDelegation>.Fail(400, "Portal foydalanuvchingiz xodim yozuviga bog'lanmagan");
        if (delegatorId != actor.IdentityId && !actor.IsAdmin)
            return ReviewResult<ApproverDelegation>.Fail(403, "Boshqa xodim uchun o'rinbosarni faqat administrator tayinlaydi");
        if (input.DelegateId == delegatorId) return ReviewResult<ApproverDelegation>.Fail(400, "O'zingizni o'rinbosar qilib bo'lmaydi");
        var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        if (reason?.Length > 500) return ReviewResult<ApproverDelegation>.Fail(400, "Izoh ko'pi bilan 500 belgi");
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Tashkent).DateTime);
        if (input.From < today || input.Until < input.From || input.Until > today.AddDays(MaxDays))
            return ReviewResult<ApproverDelegation>.Fail(400, $"Muddat: bugundan boshlab, ko'pi bilan {MaxDays} kun");

        var people = await db.Identities.AsNoTracking().Where(i => i.Id == delegatorId || i.Id == input.DelegateId)
            .Select(i => new { i.Id, i.EmployeeNumber, i.Status }).ToDictionaryAsync(i => i.Id, ct);
        if (!people.TryGetValue(delegatorId.Value, out var delegator) || !people.TryGetValue(input.DelegateId, out var @delegate))
            return ReviewResult<ApproverDelegation>.Fail(404, "Xodim topilmadi");
        if (@delegate.Status is not IdentityStatus.Active) return ReviewResult<ApproverDelegation>.Fail(400, "O'rinbosar ishlayotgan xodim bo'lishi kerak");

        var (from, to) = (input.From == today ? now : StartOf(input.From), StartOf(input.Until.AddDays(1)));
        if (await db.ApproverDelegations.AnyAsync(d => d.DelegatorId == delegatorId && d.RevokedAt == null && d.ValidTo > from && d.ValidFrom < to, ct))
            return ReviewResult<ApproverDelegation>.Fail(409, "Shu davrda boshqa o'rinbosarlik bor: avval uni bekor qiling");

        var delegation = new ApproverDelegation
        {
            DelegatorId = delegatorId.Value, DelegateId = input.DelegateId, ValidFrom = from, ValidTo = to, Reason = reason,
            CreatedAt = now, CreatedBy = actor.Login, CreatedByName = actor.Name
        };
        db.ApproverDelegations.Add(delegation);
        await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, "delegation.created", "identity", delegatorId.ToString(),
            JsonSerializer.Serialize(new { delegationId = delegation.Id, delegator = delegator.EmployeeNumber, @delegate = @delegate.EmployeeNumber,
                from = input.From.ToString("yyyy-MM-dd"), until = input.Until.ToString("yyyy-MM-dd"), reason }, Json), actor.IpAddress, actor.CorrelationId), ct);
        return new(delegation);
    }

    public async Task<ReviewResult<ApproverDelegation>> RevokeAsync(RequestActor actor, Guid id, CancellationToken ct = default)
    {
        var delegation = await db.ApproverDelegations.Include(d => d.Delegator).Include(d => d.Delegate).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delegation is null || (delegation.DelegatorId != actor.IdentityId && !actor.IsAdmin)) return ReviewResult<ApproverDelegation>.Fail(404, "O'rinbosarlik topilmadi");
        var now = time.GetUtcNow();
        if (delegation.RevokedAt is not null || delegation.ValidTo <= now) return ReviewResult<ApproverDelegation>.Fail(409, "O'rinbosarlik allaqachon tugagan");
        (delegation.RevokedAt, delegation.RevokedBy) = (now, actor.Login);
        await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, "delegation.revoked", "identity", delegation.DelegatorId.ToString(),
            JsonSerializer.Serialize(new { delegationId = delegation.Id, delegator = delegation.Delegator.EmployeeNumber, @delegate = delegation.Delegate.EmployeeNumber }, Json),
            actor.IpAddress, actor.CorrelationId), ct);
        return new(delegation);
    }
}
