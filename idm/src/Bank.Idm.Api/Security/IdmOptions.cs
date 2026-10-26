using Bank.Idm.Infrastructure.Sync;

namespace Bank.Idm.Api.Security;

public class IdmOptions
{
    public const string Section = "Idm";
    public SsoOptions Sso { get; set; } = new();
    /// <summary>Birinchi ishga tushishda IdM administratori qilinadigan loginlar.</summary>
    public string[] BootstrapAdmins { get; set; } = [];
    /// <summary>Ishga tushishda migratsiyalarni qo'llash (ishlab chiqarishda alohida bosqichda qilinadi).</summary>
    public bool MigrateOnStartup { get; set; }
    /// <summary>Ulangan tizimlar: kalit — tizim kodi (masalan "AD").</summary>
    public Dictionary<string, ConnectorDefinition> Connectors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tizimga yozish aylanishi oralig'i (reja + navbat); 0 — fon ishlovchisi o'chiq.</summary>
    public int ProvisioningIntervalSeconds { get; set; } = 30;
}

public class SsoOptions
{
    /// <summary>SimpleIdServer realm manzili, masalan https://localhost:5001/master</summary>
    public string Authority { get; set; } = null!;
    /// <summary>Tokenning aud qiymati (portal ilovasi).</summary>
    public string[] Audiences { get; set; } = ["idm-portal"];
    /// <summary>API'ga kirish uchun tokendagi majburiy ruxsat doirasi.</summary>
    public string RequiredScope { get; set; } = "idm";
    /// <summary>Login olinadigan claim.</summary>
    public string LoginClaim { get; set; } = "sub";
    /// <summary>Faqat ishlab chiqish: o'z-o'zidan imzolangan IdServer sertifikatiga ishonish.</summary>
    public bool AllowUntrustedCertificate { get; set; }
}
