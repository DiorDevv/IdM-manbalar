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
    /// <summary>TLS'ni tugatuvchi teskari proksi(lar) IP manzillari: faqat ulardan X-Forwarded-For/Proto qabul qilinadi.</summary>
    public string[] TrustedProxies { get; set; } = [];
    /// <summary>Bir nechta server nusxasi: yurak urishi va to'xtagan nusxani aniqlash.</summary>
    public Sync.ClusterOptions Cluster { get; set; } = new();
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
    public AdminMfaOptions AdminMfa { get; set; } = new();
}

/// <summary>
/// Imtiyozli portal vakolatlari (administrator, auditor) faqat ikki bosqichli kirish bilan: access token'da ikkinchi omil
/// (<c>amr</c>) yoki ruxsat etilgan kirish darajasi (<c>acr</c>) bo'lmasa, vakolat berilmaydi — foydalanuvchi oddiy xodim sifatida ishlaydi.
/// </summary>
public class AdminMfaOptions
{
    public bool Required { get; set; }
    public string[] Roles { get; set; } = [];
    public static readonly string[] DefaultRoles = [Bank.Idm.Domain.PortalRoles.Admin, Bank.Idm.Domain.PortalRoles.Auditor];
    /// <summary>Ikkinchi omil sifatida qabul qilinadigan amr qiymatlari (RFC 8176 va SimpleIdServer nomlari).</summary>
    public string[] AcceptedAmr { get; set; } = [];
    public static readonly string[] DefaultAmr = ["otp", "mfa", "hwk", "swk", "fido", "webauthn", "sms", "mobile"];
    public string[] AcceptedAcr { get; set; } = [];
    /// <summary>
    /// SSO'dagi ikki bosqichli kirish darajasi (ACR nomi, masalan "idm-mfa": parol + bir martalik kod). Portal vakolat yetmaganda
    /// shu qiymat bilan qayta kirishni so'raydi (acr_values); token'dagi shu acr ham qabul qilinadi.
    /// </summary>
    public string? StepUpAcr { get; set; }

    // Massiv standartlari alohida: .NET konfiguratsiyasi massivni almashtirmaydi, indeks bo'yicha ustiga yozadi
    public IReadOnlyList<string> EffectiveRoles => Roles.Length > 0 ? Roles : DefaultRoles;
    public IReadOnlyList<string> EffectiveAmr => AcceptedAmr.Length > 0 ? AcceptedAmr : DefaultAmr;
}
