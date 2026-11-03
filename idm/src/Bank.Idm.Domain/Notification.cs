namespace Bank.Idm.Domain;

/// <summary>
/// Xabarnoma navbati (outbox): rejalashtiruvchi yozadi, jo'natuvchi yuboradi va xatoda qayta uradi. DedupKey — bir hodisa uchun
/// bir xabar (masalan "request-step:{bosqich}:{qabul qiluvchi}"), server qayta ishga tushsa ham takrorlanmaydi.
/// Matnda shaxsiy ma'lumot minimal: rol nomi, xodim ismi va portal havolasi (asos, izohlar — faqat portalda).
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string DedupKey { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public Guid? RecipientId { get; set; }
    public string Email { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string Body { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>Urinishlar tugadi: boshqa yuborilmaydi (metrikada ko'rinadi).</summary>
    public DateTimeOffset? FailedAt { get; set; }
}
