namespace Bank.Idm.Connectors;

/// <summary>
/// Maqsadli tizim (AD, ABS, pochta...) bilan ishlash moduli. Har bir tizim alohida konnektor.
/// Hozircha faqat o'qish; yozish (yaratish, bloklash, guruhga qo'shish) G bosqichida qo'shiladi.
/// </summary>
public interface IConnector
{
    /// <summary>Tizimdagi barcha akkaunt va guruhlarning joriy holati.</summary>
    Task<ConnectorSnapshot> ReadAsync(CancellationToken ct = default);
}

public record ConnectorSnapshot(IReadOnlyList<ExternalAccount> Accounts, IReadOnlyList<ExternalGroup> Groups)
{
    /// <summary>O'qib bo'lmagan yozuvlar (masalan identifikatorsiz): ishga tushish tarixida ko'rsatiladi.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Tizimdagi akkaunt: IdM'ga kerakli maydonlar, tizim formatidan qat'i nazar.</summary>
public record ExternalAccount(
    string ExternalId,
    string Login,
    bool IsEnabled,
    string? EmployeeNumber = null,
    string? FirstName = null,
    string? LastName = null,
    string? DisplayName = null,
    string? Email = null,
    string? Department = null,
    string? JobTitle = null,
    /// <summary>Rahbar akkauntining DN'i (AD "manager" atributi).</summary>
    string? ManagerDn = null,
    string? DistinguishedName = null,
    IReadOnlyList<string>? GroupIds = null)
{
    public IReadOnlyList<string> GroupIds { get; init; } = GroupIds ?? [];
}

public record ExternalGroup(string ExternalId, string Name, string? Description = null, string? DistinguishedName = null);

/// <summary>Konnektor tizimga ulana olmadi yoki tizim noto'g'ri javob berdi (xabar portal va auditda ko'rinadi).</summary>
public class ConnectorException(string message, Exception? inner = null) : Exception(message, inner);
