namespace Bank.Idm.Domain;

/// <summary>Maqsadli tizim: AD, ABS, pochta va boshqalar. Har biri alohida konnektor orqali ulanadi.</summary>
public class Application
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Qisqa kod, masalan "AD" yoki "ABS".</summary>
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public ApplicationKind Kind { get; set; }
    /// <summary>Resurs egasi: ruxsat so'rovlarini tasdiqlaydi.</summary>
    public Guid? OwnerId { get; set; }
    public Identity? Owner { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Medium;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Account> Accounts { get; set; } = [];
    public List<Entitlement> Entitlements { get; set; } = [];
}

public enum ApplicationKind
{
    Directory = 0,
    CoreBanking = 1,
    Database = 2,
    WebApplication = 3,
    /// <summary>Kadrlar tizimi: xodimlar haqidagi haqiqat manbai (akkauntlari yo'q).</summary>
    HumanResources = 4,
    Other = 9
}

public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
