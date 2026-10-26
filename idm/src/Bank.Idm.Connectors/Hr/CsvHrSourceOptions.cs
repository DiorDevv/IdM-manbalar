namespace Bank.Idm.Connectors.Hr;

/// <summary>
/// HR eksport fayli (CSV, UTF-8). Ustun nomlari sozlanadi: HR tizimi qanday sarlavha bersa, shunisi yoziladi.
/// Ajratuvchi (";" yoki ",") sarlavha qatoridan aniqlanadi.
/// </summary>
public class CsvHrSourceOptions
{
    public string Path { get; set; } = null!;
    /// <summary>Juda katta yoki noto'g'ri fayldan himoya.</summary>
    public long MaxFileBytes { get; set; } = 50 * 1024 * 1024;
    /// <summary>Sana formatlari (birinchi mos kelgani olinadi).</summary>
    public string[] DateFormats { get; set; } = ["dd.MM.yyyy", "yyyy-MM-dd"];
    public CsvHrColumns Columns { get; set; } = new();
    /// <summary>Status ustunidagi qiymatlar (katta-kichik harf farqsiz).</summary>
    public string[] ActiveValues { get; set; } = ["active", "faol", "ishlaydi"];
    public string[] SuspendedValues { get; set; } = ["suspended", "to'xtatilgan", "ta'til", "dekret"];
    public string[] TerminatedValues { get; set; } = ["terminated", "ishdan ketgan", "bo'shatilgan"];
}

public class CsvHrColumns
{
    public string EmployeeNumber { get; set; } = "employeeNumber";
    public string FirstName { get; set; } = "firstName";
    public string LastName { get; set; } = "lastName";
    public string? MiddleName { get; set; } = "middleName";
    public string? Email { get; set; } = "email";
    public string? Department { get; set; } = "department";
    public string? JobTitle { get; set; } = "jobTitle";
    public string? ManagerEmployeeNumber { get; set; } = "managerEmployeeNumber";
    public string? HireDate { get; set; } = "hireDate";
    public string? TerminationDate { get; set; } = "terminationDate";
    public string? Status { get; set; } = "status";
}
