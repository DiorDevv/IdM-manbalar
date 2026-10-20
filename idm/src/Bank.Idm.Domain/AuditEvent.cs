namespace Bank.Idm.Domain;

/// <summary>
/// Audit jurnali yozuvi. Faqat qo'shiladi: bazada UPDATE/DELETE trigger bilan taqiqlangan,
/// har bir yozuv oldingisining xeshini saqlaydi (zanjir), shuning uchun yozuvni jimgina o'zgartirib bo'lmaydi.
/// </summary>
public class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    /// <summary>Kim bajardi: SSO subject yoki "system".</summary>
    public string ActorId { get; set; } = null!;
    public string? ActorName { get; set; }
    /// <summary>Masalan "identity.created", "account.disabled".</summary>
    public string Action { get; set; } = null!;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    /// <summary>Qo'shimcha ma'lumot (JSON): eski va yangi qiymatlar va h.k.</summary>
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
    public string PreviousHash { get; set; } = null!;
    public string Hash { get; set; } = null!;
}
