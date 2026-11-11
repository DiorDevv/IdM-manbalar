using System.Text.Json;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Requests;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Reviews;

public enum ReviewFrequency { Monthly = 0, Quarterly = 1 }

/// <summary>
/// Takrorlanadigan attestatsiya (Markaziy bank nizomi, 44-band: huquqlar kamida oyda bir marta qayta ko'rib chiqiladi).
/// Sozlamada (<c>Idm:ReviewSchedules:{kalit}</c>), portalda emas: jadvalni o'chirish o'zgarishlarni boshqarish jarayonidan o'tadi.
/// </summary>
public class ReviewScheduleDefinition
{
    public string? Name { get; set; }
    public bool Enabled { get; set; } = true;
    public ReviewFrequency Frequency { get; set; } = ReviewFrequency.Monthly;
    /// <summary>Davrning nechanchi kuni boshlanadi (1–28; server shu kuni ishlamagan bo'lsa — davr ichida keyinroq).</summary>
    public int DayOfMonth { get; set; } = 1;
    /// <summary>Ko'rib chiqish muddati, kun.</summary>
    public int DurationDays { get; set; } = 14;
    /// <summary>
    /// To'liq ko'rib chiqish shuncha oyda bir (1 — har safar to'liq). Qolgan oylarda faqat o'zgarishlar: oldingi kampaniyadan keyin
    /// berilgan ruxsatlar va barcha imtiyozlilar — rahbar har oy yuzlab o'zgarmagan yozuvni "ko'r-ko'rona" tasdiqlamasligi uchun.
    /// </summary>
    public int FullEveryMonths { get; set; } = 1;
    public string? Department { get; set; }
    /// <summary>Tizim kodi (masalan "AD"); bo'sh — barcha tizimlar.</summary>
    public string? Application { get; set; }
    public bool PrivilegedOnly { get; set; }
    public bool RevokeUnreviewed { get; set; }
}

/// <summary>Jadvalning joriy davri: boshlanish kuni, nomi va bu davrda to'liq yoki faqat o'zgarishlar.</summary>
public record SchedulePeriod(DateOnly Start, DateOnly RunOn, string Label, bool Full);

public record ScheduleRunResult(string Key, string Period, string Outcome, Guid? CampaignId = null, int Items = 0, string? Error = null);

public class ReviewScheduler(IdmDbContext db, AccessReviewService reviews, AuditLog audit, TimeProvider time)
{
    public const string ActorPrefix = "schedule:";
    private static readonly TimeZoneInfo Tashkent = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Tashkent).DateTime);

    public static string? Validate(string key, ReviewScheduleDefinition d)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z0-9_-]{1,40}$")) return $"{key}: kalit faqat lotin harf, raqam, - va _ (≤ 40)";
        if (d.DayOfMonth is < 1 or > 28) return $"{key}: DayOfMonth 1–28 bo'lishi kerak";
        if (d.DurationDays is < 1 or > 90) return $"{key}: DurationDays 1–90 bo'lishi kerak";
        if (d.FullEveryMonths is < 1 or > 12) return $"{key}: FullEveryMonths 1–12 bo'lishi kerak";
        return null;
    }

    /// <summary>Bugungi sana qaysi davrga tushadi. Choraklik — yanvar, aprel, iyul, oktabr.</summary>
    public static SchedulePeriod PeriodOf(ReviewScheduleDefinition d, DateOnly today)
    {
        var month = d.Frequency == ReviewFrequency.Quarterly ? (today.Month - 1) / 3 * 3 + 1 : today.Month;
        var start = new DateOnly(today.Year, month, 1);
        var label = d.Frequency == ReviewFrequency.Quarterly ? $"{today.Year}-Q{(month - 1) / 3 + 1}" : $"{today.Year}-{month:D2}";
        return new SchedulePeriod(start, start.AddDays(d.DayOfMonth - 1), label, (month - 1) % Math.Max(1, d.FullEveryMonths) == 0);
    }

    /// <summary>Navbatdagi ishga tushish kuni (portal uchun).</summary>
    public static DateOnly NextRunOn(ReviewScheduleDefinition d, DateOnly today, bool currentDone)
    {
        var p = PeriodOf(d, today);
        if (!currentDone && today <= p.RunOn) return p.RunOn;
        if (!currentDone) return today;
        var next = p.Start.AddMonths(d.Frequency == ReviewFrequency.Quarterly ? 3 : 1);
        return next.AddDays(d.DayOfMonth - 1);
    }

    public static string Actor(string key) => ActorPrefix + key;

    /// <summary>Shu davr uchun kampaniya ochilganmi yoki "ko'rib chiqiladigan narsa yo'q" deb qayd etilganmi.</summary>
    public async Task<bool> IsDoneAsync(string key, SchedulePeriod period, CancellationToken ct)
    {
        var actor = Actor(key);
        var from = new DateTimeOffset(period.Start.ToDateTime(TimeOnly.MinValue), Tashkent.GetUtcOffset(period.Start.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        if (await db.AccessReviewCampaigns.AnyAsync(c => c.CreatedBy == actor && c.CreatedAt >= from, ct)) return true;
        return await db.AuditEvents.AnyAsync(e => e.Action == "review.schedule-skipped" && e.TargetType == "review-schedule" && e.TargetId == key
            && e.OccurredAt >= from, ct);
    }

    /// <summary>Muddati kelgan jadvallar bo'yicha kampaniya ochadi (faqat yetakchi nusxada chaqiriladi; takror chaqirish xavfsiz).</summary>
    public async Task<IReadOnlyList<ScheduleRunResult>> RunDueAsync(IReadOnlyDictionary<string, ReviewScheduleDefinition> schedules, CancellationToken ct = default)
    {
        var results = new List<ScheduleRunResult>();
        var today = Today;
        foreach (var (key, d) in schedules.OrderBy(s => s.Key))
        {
            if (!d.Enabled || Validate(key, d) is not null) continue;
            var period = PeriodOf(d, today);
            if (today < period.RunOn || await IsDoneAsync(key, period, ct)) continue;
            results.Add(await RunAsync(key, d, period, ct));
        }
        return results;
    }

    private async Task<ScheduleRunResult> RunAsync(string key, ReviewScheduleDefinition d, SchedulePeriod period, CancellationToken ct)
    {
        var actor = new RequestActor(Actor(key), "IdM (attestatsiya jadvali)", null, IsAdmin: true);
        Guid? appId = null;
        if (d.Application is { Length: > 0 } code)
        {
            appId = await db.Applications.Where(a => a.Code == code).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (appId is null) return new ScheduleRunResult(key, period.Label, "error", Error: $"Tizim topilmadi: {code}");
        }
        // To'liq bo'lmagan davr: oldingi kampaniyadan beri berilganlar (birinchi marta — to'liq)
        var previous = await db.AccessReviewCampaigns.Where(c => c.CreatedBy == actor.Login && c.Status != AccessReviewStatus.Cancelled)
            .OrderByDescending(c => c.CreatedAt).Select(c => (DateTimeOffset?)c.CreatedAt).FirstOrDefaultAsync(ct);
        var full = period.Full || previous is null;
        var name = $"{(string.IsNullOrWhiteSpace(d.Name) ? key : d.Name.Trim())} · {period.Label}";
        var description = full ? "Jadval bo'yicha: to'liq ko'rib chiqish"
            : $"Jadval bo'yicha: {TimeZoneInfo.ConvertTime(previous!.Value, Tashkent):dd.MM.yyyy} dan keyin berilgan ruxsatlar va barcha imtiyozli ruxsatlar";
        var scope = new CampaignScope(name, description, d.Department, appId, d.PrivilegedOnly, Today.AddDays(d.DurationDays), d.RevokeUnreviewed,
            full ? null : previous);
        var created = await reviews.CreateAsync(actor, scope, ct);
        if (created.Value is { } campaign) return new ScheduleRunResult(key, period.Label, full ? "full" : "changes", campaign.Id, campaign.Items.Count);
        // Ko'rib chiqiladigan narsa yo'q (o'tgan oydan beri o'zgarish yo'q) — davr qayd etilib yopiladi (hisobotda ko'rinadi).
        // Boshqa xato (noto'g'ri sozlama) — davr yopilmaydi: sozlama tuzatilgach shu davrning o'zida ochiladi, oy tushib qolmaydi
        if (created.Error == AccessReviewService.NothingToReview) return await SkipAsync(key, period, ct);
        return new ScheduleRunResult(key, period.Label, "error", Error: created.Error);
    }

    private async Task<ScheduleRunResult> SkipAsync(string key, SchedulePeriod period, CancellationToken ct)
    {
        await audit.SaveChangesAsync(new AuditEntry(Actor(key), "IdM (attestatsiya jadvali)", "review.schedule-skipped", "review-schedule", key,
            JsonSerializer.Serialize(new { period = period.Label, reason = AccessReviewService.NothingToReview })), ct);
        return new ScheduleRunResult(key, period.Label, "empty");
    }
}
