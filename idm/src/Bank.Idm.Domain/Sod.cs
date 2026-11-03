namespace Bank.Idm.Domain;

/// <summary>
/// Vazifalarni ajratish (SoD) qoidasi: bir xodimda ikki tomondagi ruxsatlardan bir vaqtda bo'lishi mumkin emas
/// (masalan "kredit beradi" va "kreditni tasdiqlaydi"). Ruxsat darajasida: haqiqiy holat (AD guruhlari) bo'yicha tekshiriladi.
/// </summary>
public class SodRule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.High;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = null!;

    public List<SodRuleEntitlement> Entitlements { get; set; } = [];
}

public class SodRuleEntitlement
{
    public Guid SodRuleId { get; set; }
    public SodRule SodRule { get; set; } = null!;
    public Guid EntitlementId { get; set; }
    public Entitlement Entitlement { get; set; } = null!;
    public SodSide Side { get; set; }
}

public enum SodSide
{
    Left = 0,
    Right = 1
}

/// <summary>
/// SoD istisnosi: aniq bir xodimda aniq bir qoida buzilishiga vaqtincha ruxsat (masalan kichik filialda odam yetishmaydi).
/// Asos va kompensatsion nazorat (masalan "har kuni bo'lim boshlig'i operatsiyalarni tekshiradi") majburiy, muddat — ko'pi bilan
/// bir yil. Muddat tugasa yoki bekor qilinsa, buzilish yana ochiq hisoblanadi. O'zgartirilmaydi: yangi muddat — yangi istisno.
/// </summary>
public class SodException
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SodRuleId { get; set; }
    public SodRule SodRule { get; set; } = null!;
    public Guid IdentityId { get; set; }
    public Identity Identity { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string CompensatingControl { get; set; } = null!;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public string? CreatedByName { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevokedByName { get; set; }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ValidFrom <= now && ValidTo > now;
}
