namespace Bank.Idm.Domain;

/// <summary>
/// Holat ko'rsatkichining kunlik qiymati (egasiz akkauntlar, ochiq SoD...): bosh sahifadagi trend va sparkline'lar uchun.
/// Kun ichida bir necha marta yangilanadi (oxirgi qiymat qoladi). Shaxsiy ma'lumot yo'q — faqat sonlar.
/// </summary>
public class MetricSnapshot
{
    public DateOnly Day { get; set; }
    public string Key { get; set; } = null!;
    public double Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class MetricKeys
{
    public const string LeaversActive = "leavers_active";
    public const string OrphanAccounts = "orphan_accounts";
    public const string SodOpen = "sod_open";
    public const string PendingRequests = "pending_requests";
    public const string ExcessAccess = "excess_access";
    public static readonly string[] All = [LeaversActive, OrphanAccounts, SodOpen, PendingRequests, ExcessAccess];
}
