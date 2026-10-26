using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Hr;
using Bank.Idm.Connectors.Ldap;
using Bank.Idm.Domain;

namespace Bank.Idm.Infrastructure.Sync;

/// <summary>
/// Konnektor sozlamasi (Idm:Connectors:{tizim kodi}). Sirlar (BindPassword) faqat muhit o'zgaruvchisi yoki sirlar
/// saqlovchisidan beriladi, bazaga yozilmaydi.
/// </summary>
public class ConnectorDefinition
{
    public bool Enabled { get; set; } = true;
    /// <summary>"ldap" (Active Directory, OpenLDAP) yoki "hr-csv" (kadrlar tizimi eksport fayli).</summary>
    public string Type { get; set; } = ConnectorTypes.Ldap;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    /// <summary>Jadval bo'yicha sinxronlash oralig'i; 0 bo'lsa faqat qo'lda.</summary>
    public int IntervalMinutes { get; set; } = 15;
    /// <summary>Bir ishga tushishda shundan ko'p foiz akkaunt yo'qolsa, o'zgarishlar qo'llanmaydi (noto'g'ri OU yoki filtrdan himoya).</summary>
    public int MaxDeletePercent { get; set; } = 20;
    /// <summary>Tabel raqami bor, lekin IdM'da yo'q xodimni shu tizim ma'lumotlaridan yaratish (HR ulanguncha, D bosqichi).</summary>
    public bool CreateIdentities { get; set; } = true;
    /// <summary>Imtiyozli deb belgilanadigan guruhlar nomi (masalan "Domain Admins").</summary>
    public string[] PrivilegedGroups { get; set; } = [];
    public LdapConnectorOptions Ldap { get; set; } = new();

    /// <summary>HR: faylda yo'q xodimni ishdan ketgan deb belgilash. Standart — yo'q (faqat ogohlantirish): eksport to'liq ekaniga ishonch bo'lganda yoqiladi.</summary>
    public bool TerminateMissing { get; set; }
    /// <summary>HR sanalari (ishga kirish, ishdan ketish) qaysi vaqt mintaqasida "bugun" bo'lishi.</summary>
    public string TimeZone { get; set; } = "Asia/Tashkent";
    public CsvHrSourceOptions Csv { get; set; } = new();
    /// <summary>Tizimga yozish (G bosqichi). Standart — o'chiq.</summary>
    public ProvisioningDefinition Provisioning { get; set; } = new();

    public bool IsHr => Type.Equals(ConnectorTypes.HrCsv, StringComparison.OrdinalIgnoreCase);
    public ApplicationKind Kind => IsHr ? ApplicationKind.HumanResources : ApplicationKind.Directory;
    public SyncSettings ToSettings() => new(MaxDeletePercent, CreateIdentities, PrivilegedGroups);
    public HrSettings ToHrSettings() => new(MaxDeletePercent, TerminateMissing, TimeZone);
    public bool CanProvision => !IsHr && Provisioning.Mode != ProvisioningMode.Off;
    public Provisioning.ProvisioningSettings ToProvisioningSettings() => new(Provisioning.Mode, Provisioning.CreateAccounts,
        Provisioning.MaxAutoOperations, Provisioning.ProtectedLogins, Provisioning.DisableSuspended, Provisioning.MaxAttempts, Ldap.LoginMaxLength);
}

public enum ProvisioningMode
{
    Off = 0,
    /// <summary>Dry-run: har bir amal administrator tasdig'idan keyin bajariladi.</summary>
    Approval = 1,
    /// <summary>Darhol bajariladi; bir rejada MaxAutoOperations'dan ko'p amal bo'lsa — hammasi tasdiqqa to'xtatiladi.</summary>
    Automatic = 2
}

public class ProvisioningDefinition
{
    public ProvisioningMode Mode { get; set; } = ProvisioningMode.Off;
    /// <summary>Rol kerak, lekin tizimda akkaunti yo'q xodimga akkaunt yaratish (bloklangan, parolsiz).</summary>
    public bool CreateAccounts { get; set; }
    public int MaxAutoOperations { get; set; } = 20;
    /// <summary>Hech qachon o'zgartirilmaydigan akkauntlar (masalan administrator, xizmat akkauntlari).</summary>
    public string[] ProtectedLogins { get; set; } = [];
    /// <summary>Vaqtincha to'xtatilgan xodim akkauntini bloklash.</summary>
    public bool DisableSuspended { get; set; } = true;
    public int MaxAttempts { get; set; } = 5;
}

public static class ConnectorTypes
{
    public const string Ldap = "ldap";
    public const string HrCsv = "hr-csv";
}

public record SyncSettings(int MaxDeletePercent = 20, bool CreateIdentities = true, IReadOnlyCollection<string>? PrivilegedGroups = null);

public interface IConnectorFactory
{
    IConnector Create(string code, ConnectorDefinition definition);
    IHrSource CreateHrSource(string code, ConnectorDefinition definition);
    IProvisioningConnector CreateProvisioner(string code, ConnectorDefinition definition);
}

public class ConnectorFactory : IConnectorFactory
{
    public IConnector Create(string code, ConnectorDefinition definition) => definition.Type.ToLowerInvariant() switch
    {
        ConnectorTypes.Ldap => new LdapConnector(definition.Ldap),
        _ => throw new ConnectorException($"{code}: konnektor turi noma'lum: {definition.Type}")
    };

    public IProvisioningConnector CreateProvisioner(string code, ConnectorDefinition definition) => definition.Type.ToLowerInvariant() switch
    {
        ConnectorTypes.Ldap => new LdapProvisioner(definition.Ldap),
        _ => throw new ConnectorException($"{code}: {definition.Type} konnektori tizimga yozishni qo'llamaydi")
    };

    public IHrSource CreateHrSource(string code, ConnectorDefinition definition) => definition.IsHr
        ? new CsvHrSource(definition.Csv)
        : throw new ConnectorException($"{code}: HR manbai emas: {definition.Type}");
}
