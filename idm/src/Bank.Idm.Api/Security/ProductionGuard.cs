using Bank.Idm.Infrastructure.Sync;
using Npgsql;

namespace Bank.Idm.Api.Security;

/// <summary>
/// Ishlab chiqarish muhitida xavfsiz bo'lmagan sozlama bilan server ishga tushmaydi (xato ro'yxati bilan to'xtaydi):
/// shifrlanmagan yoki sertifikati tekshirilmaydigan ulanishlar, sozlama faylidagi sirlar, ishonchli proksisiz ochiq HTTP.
/// Ogohlantirishlar (masalan ishga tushishda migratsiya) jurnalga yoziladi.
/// </summary>
public static class ProductionGuard
{
    /// <summary>Sir hisoblanadigan kalitlar (oxiri bo'yicha): faqat muhit o'zgaruvchisi yoki sirlar katalogidan (Vault, Kubernetes secrets).</summary>
    private static readonly string[] SecretSuffixes = ["BindPassword", "WriteBindPassword", "Password", "Secret", "Token", "MetricsToken"];

    public record Result(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    public static Result Check(IConfiguration configuration, IdmOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!Uri.TryCreate(options.Sso.Authority, UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps)
            errors.Add($"Idm:Sso:Authority HTTPS bo'lishi kerak (hozir: {options.Sso.Authority})");
        if (options.Sso.AllowUntrustedCertificate) errors.Add("Idm:Sso:AllowUntrustedCertificate yoqilgan");

        var connection = configuration.GetConnectionString("Idm");
        if (connection is not null)
        {
            var csb = new NpgsqlConnectionStringBuilder(connection);
            if (csb.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))
                errors.Add($"ConnectionStrings:Idm: SSL Mode=VerifyFull (yoki VerifyCA) bo'lishi kerak, hozir {csb.SslMode} — sertifikat tekshirilmasa, ulanishni o'g'irlash mumkin");
            if (!string.IsNullOrEmpty(csb.Password) && FromJsonFile(configuration, "ConnectionStrings:Idm"))
                errors.Add("ConnectionStrings:Idm paroli sozlama faylida: muhit o'zgaruvchisi yoki sirlar katalogi orqali bering");
        }

        foreach (var (code, c) in options.Connectors.Where(x => x.Value.Enabled && !x.Value.IsHr))
        {
            if (!c.Ldap.UseSsl) errors.Add($"Idm:Connectors:{code}: LDAPS (UseSsl=true) majburiy — oddiy LDAP'da parol ochiq uzatiladi");
            if (c.Provisioning.Mode == ProvisioningMode.Automatic && c.Ldap.WriteBindDn is null)
                warnings.Add($"Idm:Connectors:{code}: avtomatik yozish o'qish akkaunti bilan — eng kam huquq uchun alohida WriteBindDn tavsiya etiladi");
        }

        foreach (var key in SecretKeys(configuration).Where(k => FromJsonFile(configuration, k)))
            errors.Add($"{key} sozlama faylida: sirlar muhit o'zgaruvchisi yoki sirlar katalogi (Idm__SecretsDirectory) orqali beriladi");

        // Manzil berilmasa Kestrel http://localhost:5000 da ishlaydi
        var urls = (configuration["urls"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Concat(configuration.GetSection("Kestrel:Endpoints").GetChildren().Select(e => e["Url"]).OfType<string>()).ToList();
        if ((urls.Count == 0 || urls.Any(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))) && options.TrustedProxies.Length == 0)
            errors.Add("HTTP manzil ochiq, lekin Idm:TrustedProxies bo'sh: HTTPS'ni Kestrel'da yoqing yoki TLS'ni tugatuvchi proksi manzilini ko'rsating");

        if (!options.Siem.Enabled) warnings.Add("Idm:Siem o'chiq: audit faqat IdM bazasida (bank talabi — markaziy SIEM)");
        else if (options.Siem.Protocol != Bank.Idm.Infrastructure.Siem.SiemProtocols.TcpTls)
            errors.Add($"Idm:Siem:Protocol={options.Siem.Protocol}: audit faqat TLS orqali yuboriladi (tcp-tls)");
        if (options.MetricsToken is { Length: > 0 and < 32 }) errors.Add("Idm:MetricsToken kamida 32 belgi bo'lishi kerak");
        if (options.MigrateOnStartup) warnings.Add("Idm:MigrateOnStartup yoqilgan: ishlab chiqarishda migratsiya alohida, nazorat ostida qo'llanadi");
        if (options.BootstrapAdmins.Length > 0) warnings.Add("Idm:BootstrapAdmins berilgan: birinchi ishga tushishdan keyin olib tashlang");
        if (!options.Sso.AdminMfa.Required) warnings.Add("Idm:Sso:AdminMfa:Required o'chiq: administratorlar ikki bosqichli kirishsiz ishlaydi");
        return new Result(errors, warnings);
    }

    private static IEnumerable<string> SecretKeys(IConfiguration configuration) =>
        configuration.AsEnumerable().Where(kv => !string.IsNullOrEmpty(kv.Value)
            && SecretSuffixes.Any(s => kv.Key.EndsWith(":" + s, StringComparison.OrdinalIgnoreCase))).Select(kv => kv.Key);

    /// <summary>Qiymat qaysi manbadan kelgan: oxirgi (eng ustun) manba sozlama fayli (appsettings*.json va h.k.) bo'lsa — true.</summary>
    internal static bool FromJsonFile(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root) return false;
        foreach (var provider in root.Providers.Reverse())
            if (provider.TryGet(key, out _)) return provider is FileConfigurationProvider;
        return false;
    }
}
