namespace Bank.Idm.Domain;

/// <summary>
/// Attestatsiya kampaniyasi: rahbarlar o'z xodimlarining ruxsatlarini ko'rib chiqadi — qoldiradi yoki olib tashlaydi.
/// Ishga tushganda qamrovdagi ruxsatlar yozuvlarga ko'chiriladi (snapshot): kampaniya davomida va keyin natija o'zgarmaydi.
/// </summary>
public class AccessReviewCampaign
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public AccessReviewStatus Status { get; set; } = AccessReviewStatus.Active;
    /// <summary>Qamrov: shu bo'lim xodimlari (bo'sh — hammasi).</summary>
    public string? Department { get; set; }
    /// <summary>Qamrov: shu tizim ruxsatlari (bo'sh — hammasi).</summary>
    public Guid? ApplicationId { get; set; }
    public Application? Application { get; set; }
    /// <summary>Faqat imtiyozli ruxsatlar va yuqori xavfli rollar.</summary>
    public bool PrivilegedOnly { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public string? CreatedByName { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    public uint Version { get; set; }

    public List<AccessReviewItem> Items { get; set; } = [];
}

public enum AccessReviewStatus
{
    Active = 0,
    /// <summary>Yakunlandi: qaror qilinmagan yozuvlar "ko'rib chiqilmagan" bo'lib qoladi (hisobotda).</summary>
    Completed = 1,
    Cancelled = 2
}

/// <summary>
/// Ko'rib chiqiladigan bitta ruxsat: xodimning roli (ariza yoki qo'lda berilgan) yoki hech qaysi rol bilan asoslanmagan
/// tizim guruhi ("ortiqcha"). Avtomatik (tug'ma) rollar ko'rib chiqilmaydi: ular qoida bilan beriladi va qoida o'zgarsa o'zi olinadi.
/// </summary>
public class AccessReviewItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CampaignId { get; set; }
    public AccessReviewCampaign Campaign { get; set; } = null!;
    public AccessReviewItemKind Kind { get; set; }
    public Guid IdentityId { get; set; }
    public Identity Identity { get; set; } = null!;
    /// <summary>Tekshiruvchi (rahbar). Bo'sh — IdM administratorlari navbati.</summary>
    public Guid? ReviewerId { get; set; }
    public Identity? Reviewer { get; set; }
    /// <summary>Rol yozuvi uchun: xodimga berilgan rol tayinlashi.</summary>
    public Guid? IdentityRoleId { get; set; }
    public IdentityRole? IdentityRole { get; set; }
    /// <summary>Ortiqcha ruxsat uchun: akkaunt va guruh.</summary>
    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
    public Guid? EntitlementId { get; set; }
    public Entitlement? Entitlement { get; set; }

    // Snapshot: kampaniya ishga tushgan paytdagi holat
    public string ApplicationCode { get; set; } = null!;
    /// <summary>Rol nomi yoki guruh nomi.</summary>
    public string Title { get; set; } = null!;
    /// <summary>Rol kodi yoki akkaunt logini.</summary>
    public string? Subtitle { get; set; }
    public bool IsPrivileged { get; set; }

    public AccessReviewDecision Decision { get; set; } = AccessReviewDecision.Pending;
    public string? Comment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    /// <summary>Olib tashlash bajarildimi (rol tugadi yoki guruh tizimdan haqiqatda olindi).</summary>
    public DateTimeOffset? RemediatedAt { get; set; }
    public uint Version { get; set; }
}

public enum AccessReviewItemKind
{
    Role = 0,
    Entitlement = 1
}

public enum AccessReviewDecision
{
    Pending = 0,
    Keep = 1,
    Revoke = 2
}
