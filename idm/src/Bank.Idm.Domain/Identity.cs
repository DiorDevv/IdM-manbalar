namespace Bank.Idm.Domain;

/// <summary>
/// Xodim: IdM'dagi markaziy obyekt. Uning ma'lumotlari haqiqat manbaidan (HR) keladi,
/// tizimlardagi akkauntlari (AD, ABS...) unga bog'lanadi.
/// </summary>
public class Identity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Tabel raqami: HR va AD o'rtasidagi korrelyatsiya kaliti.</summary>
    public string EmployeeNumber { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? MiddleName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public Guid? ManagerId { get; set; }
    public Identity? Manager { get; set; }
    public IdentityStatus Status { get; set; } = IdentityStatus.Active;
    public IdentitySource Source { get; set; } = IdentitySource.Manual;
    public DateOnly? HireDate { get; set; }
    public DateOnly? TerminationDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Account> Accounts { get; set; } = [];
    public List<IdentityRole> Roles { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(MiddleName) ? $"{LastName} {FirstName}" : $"{LastName} {FirstName} {MiddleName}";
}

public enum IdentityStatus
{
    /// <summary>Ishga qabul qilinishi kutilmoqda (HR'da bor, hali ishlamaydi).</summary>
    Pending = 0,
    Active = 1,
    /// <summary>Vaqtincha to'xtatilgan (masalan uzoq ta'til, tergov).</summary>
    Suspended = 2,
    /// <summary>Ishdan ketgan: barcha akkauntlari yopilishi kerak.</summary>
    Terminated = 3
}

public enum IdentitySource
{
    Manual = 0,
    Hr = 1,
    ActiveDirectory = 2
}
