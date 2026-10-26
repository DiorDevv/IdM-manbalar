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
