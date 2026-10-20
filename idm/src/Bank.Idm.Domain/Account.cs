namespace Bank.Idm.Domain;

/// <summary>
/// Tizimdagi akkaunt. IdentityId bo'sh bo'lsa, akkaunt "egasiz": hech qaysi xodimga bog'lanmagan
/// (masalan ishdan ketgan xodimdan qolgan yoki xizmat akkaunti) — bu IdM'ning asosiy hisobotlaridan biri.
/// </summary>
public class Account
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public Application Application { get; set; } = null!;
    public Guid? IdentityId { get; set; }
    public Identity? Identity { get; set; }
    /// <summary>Tizimdagi o'zgarmas identifikator (AD'da objectGUID).</summary>
    public string ExternalId { get; set; } = null!;
    public string Login { get; set; } = null!;
    public AccountStatus Status { get; set; } = AccountStatus.Enabled;
    public AccountKind Kind { get; set; } = AccountKind.Personal;
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<AccountEntitlement> Entitlements { get; set; } = [];
}

public enum AccountStatus
{
    Enabled = 0,
    Disabled = 1,
    /// <summary>Tizimdan o'chirilgan, tarix uchun saqlanadi.</summary>
    Deleted = 2
}

public enum AccountKind
{
    Personal = 0,
    Service = 1,
    Privileged = 2
}
