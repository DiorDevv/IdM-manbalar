namespace Bank.Idm.Connectors.Hr;

/// <summary>Kadrlar tizimi (haqiqat manbai): xodimlar ro'yxatining joriy holati.</summary>
public interface IHrSource
{
    Task<HrSnapshot> ReadAsync(CancellationToken ct = default);
}

public record HrSnapshot(IReadOnlyList<HrRecord> Records)
{
    /// <summary>O'qib bo'lmagan qatorlar (masalan majburiy maydon yo'q yoki sana noto'g'ri).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>Faylda bor, lekin xato tufayli o'qilmagan tabel raqamlari: ular "HR'da yo'q" deb hisoblanmaydi.</summary>
    public IReadOnlySet<string> SkippedEmployeeNumbers { get; init; } = new HashSet<string>();
    /// <summary>Manba filialni beradimi. Yo'q bo'lsa xodimlarning filiali o'zgartirilmaydi (bo'sh qiymat "filial yo'q" degani emas).</summary>
    public bool HasBranch { get; init; }
}

/// <summary>HR'dagi xodim yozuvi. Status bo'sh bo'lsa, sanalardan aniqlanadi.</summary>
public record HrRecord(
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string? MiddleName = null,
    string? Email = null,
    string? Department = null,
    string? JobTitle = null,
    string? ManagerEmployeeNumber = null,
    DateOnly? HireDate = null,
    DateOnly? TerminationDate = null,
    HrStatus? Status = null,
    string? Branch = null);

public enum HrStatus
{
    Active,
    Suspended,
    Terminated
}
