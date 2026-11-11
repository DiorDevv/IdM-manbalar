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
    /// <summary>Tizimdagi ko'rinadigan ism (xodimga bog'lanmagan akkauntni tanib olish uchun).</summary>
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    /// <summary>Tizimda yozilgan tabel raqami: shu bo'yicha xodimga bog'lanadi (korrelyatsiya).</summary>
    public string? EmployeeNumber { get; set; }
    /// <summary>LDAP tizimlarida to'liq nom (DN): rahbar va a'zolik havolalari shu orqali.</summary>
    public string? DistinguishedName { get; set; }
    /// <summary>Tizimdagi bo'lim, lavozim, filial va rahbar (DN): HR bilan solishtirib, o'tishda tizimni yangilash uchun.</summary>
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? Branch { get; set; }
    public string? ManagerDn { get; set; }
    /// <summary>Yuqoridagi to'rt maydon tizimdan o'qilgan (false — yangilangan versiyadan keyin hali sinxronlanmagan: solishtirib bo'lmaydi).</summary>
    public bool OrgSynced { get; set; }
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
