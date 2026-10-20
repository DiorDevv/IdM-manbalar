namespace Bank.Idm.Domain;

/// <summary>Tizimdagi ruxsat: AD guruhi, ABS roli va h.k.</summary>
public class Entitlement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public Application Application { get; set; } = null!;
    /// <summary>Tizimdagi identifikator (AD guruhi uchun objectGUID).</summary>
    public string ExternalId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Medium;
    public bool IsPrivileged { get; set; }
}

/// <summary>Akkauntda haqiqatda bor ruxsat (tizimdan o'qilgan holat).</summary>
public class AccountEntitlement
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public Guid EntitlementId { get; set; }
    public Entitlement Entitlement { get; set; } = null!;
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
}
