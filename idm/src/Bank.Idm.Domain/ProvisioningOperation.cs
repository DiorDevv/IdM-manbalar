namespace Bank.Idm.Domain;

/// <summary>
/// Tizimga yozish amali (provisioning): "bo'lishi kerak" holatni tizimga olib borish. Rejalashtiruvchi taklif qiladi, administrator
/// tasdiqlaydi (yoki avtomatik rejimda darhol navbatga tushadi), navbat ishlovchisi bajaradi va xatoda qayta urinadi.
/// Bir obyekt ustida bir xil amal bir vaqtda faqat bitta faol yozuvda (DedupKey).
/// </summary>
public class ProvisioningOperation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public Application Application { get; set; } = null!;
    public ProvisioningKind Kind { get; set; }
    public ProvisioningStatus Status { get; set; } = ProvisioningStatus.Proposed;
    public Guid? IdentityId { get; set; }
    public Identity? Identity { get; set; }
    /// <summary>Akkaunt yaratishda bo'sh (bajarilgandan keyin yaratilgan akkaunt yoziladi).</summary>
    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
    public Guid? EntitlementId { get; set; }
    public Entitlement? Entitlement { get; set; }
    /// <summary>Nima uchun: "role" (rol bo'yicha kerak), "role-removed", "leaver", "suspended", "joiner".</summary>
    public string Reason { get; set; } = null!;
    /// <summary>Ko'rsatish uchun: login, guruh nomi, rollar (rejalashtirish paytidagi holat).</summary>
    public string? Summary { get; set; }
    /// <summary>"tizim:amal:akkaunt-yoki-xodim:ruxsat" — faol (taklif, navbat, bajarilmoqda, xato) yozuvlar orasida noyob.</summary>
    public string DedupKey { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Optimistik qulf (xmin): bir amalni ikki ishlovchi yoki ikki administrator bir vaqtda o'zgartira olmaydi.</summary>
    public uint Version { get; set; }

    public static readonly ProvisioningStatus[] ActiveStatuses =
        [ProvisioningStatus.Proposed, ProvisioningStatus.Queued, ProvisioningStatus.Running, ProvisioningStatus.Failed];
}

public enum ProvisioningKind
{
    CreateAccount = 0,
    DisableAccount = 1,
    AddMembership = 2,
    RemoveMembership = 3
}

public enum ProvisioningStatus
{
    /// <summary>Dry-run: administrator tasdig'ini kutmoqda.</summary>
    Proposed = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    /// <summary>Urinishlar tugadi yoki doimiy xato: administrator qayta ishga tushiradi yoki yopadi.</summary>
    Failed = 4,
    /// <summary>Bajarilishidan oldin keraksiz bo'lib qoldi (rol qaytarildi, holat o'zgardi).</summary>
    Obsolete = 5,
    /// <summary>Administrator yopdi.</summary>
    Cancelled = 6
}
