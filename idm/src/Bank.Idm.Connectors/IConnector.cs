namespace Bank.Idm.Connectors;

/// <summary>
/// Maqsadli tizim (AD, ABS, pochta...) bilan ishlash moduli. Har bir tizim alohida konnektor.
/// Yozish (yaratish, bloklash, guruh a'zoligi) — alohida <see cref="IProvisioningConnector"/>.
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
/// <param name="transient">Vaqtinchalik xato (tarmoq, server band): yozish amali keyinroq qayta uriniladi.</param>
public class ConnectorException(string message, Exception? inner = null, bool transient = false) : Exception(message, inner)
{
    public bool IsTransient { get; } = transient;
    /// <summary>LDAP natija kodi (bo'lsa).</summary>
    public int? ResultCode { get; init; }
}

/// <summary>
/// Tizimga yozish. Har bir amal idempotent: natija allaqachon bor bo'lsa (a'zo, bloklangan) xato emas.
/// Obyektlar o'zgarmas identifikator (ExternalId) bo'yicha topiladi: nom yoki OU o'zgargan bo'lsa ham to'g'ri obyektga yoziladi.
/// </summary>
public interface IProvisioningConnector : IDisposable
{
    /// <summary>Login bo'yicha akkaunt (yangi akkaunt uchun login bandligini tekshirish).</summary>
    Task<ExternalAccount?> FindByLoginAsync(string login, CancellationToken ct = default);
    /// <summary>Tabel raqami bo'yicha akkaunt: yaratishdan oldin — oldingi urinishda yaratilgan bo'lsa, qayta yaratilmaydi.</summary>
    Task<ExternalAccount?> FindByEmployeeNumberAsync(string employeeNumber, CancellationToken ct = default);
    /// <summary>Bloklangan holatda, parolsiz yaratadi. Natija — yaratilgan akkaunt (o'zgarmas identifikatori bilan).</summary>
    Task<ExternalAccount> CreateAccountAsync(NewAccount account, CancellationToken ct = default);
    /// <summary>true — bloklandi; false — allaqachon bloklangan edi.</summary>
    Task<bool> DisableAccountAsync(string accountId, CancellationToken ct = default);
    /// <summary>true — qo'shildi; false — allaqachon a'zo edi.</summary>
    Task<bool> AddMemberAsync(string accountId, string groupId, CancellationToken ct = default);
    /// <summary>true — olib tashlandi; false — a'zo emas edi.</summary>
    Task<bool> RemoveMemberAsync(string accountId, string groupId, CancellationToken ct = default);
}

public record NewAccount(string Login, string FirstName, string LastName, string DisplayName, string EmployeeNumber,
    string? Email = null, string? Department = null, string? JobTitle = null);
