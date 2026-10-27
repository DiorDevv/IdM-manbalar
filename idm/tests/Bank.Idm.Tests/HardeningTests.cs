using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Idm.Tests;

/// <summary>Ishlab chiqarish sozlamasi nazorati: xavfsiz bo'lmagan sozlamada server ishga tushmaydi.</summary>
public class ProductionGuardTests
{
    private static readonly Dictionary<string, string?> Secure = new()
    {
        ["Idm:Sso:Authority"] = "https://sso.bank.uz/master",
        ["Idm:Sso:AdminMfa:Required"] = "true",
        ["ConnectionStrings:Idm"] = "Host=db.bank.uz;Database=idm;Username=idm;Password=p;SSL Mode=VerifyFull",
        ["urls"] = "https://0.0.0.0:7100",
        ["Idm:Connectors:AD:Ldap:UseSsl"] = "true",
        ["Idm:Connectors:AD:Ldap:BindPassword"] = "sir",
        ["Idm:Siem:Enabled"] = "true",
        ["Idm:Siem:Host"] = "siem.bank.uz",
    };

    private static ProductionGuard.Result Check(Dictionary<string, string?> values, string? jsonFile = null)
    {
        var builder = new ConfigurationBuilder();
        if (jsonFile is not null) builder.AddJsonFile(jsonFile);
        var configuration = builder.AddInMemoryCollection(values).Build();
        return ProductionGuard.Check(configuration, configuration.GetSection(IdmOptions.Section).Get<IdmOptions>() ?? new IdmOptions());
    }

    [Fact]
    public void Secure_configuration_passes()
    {
        var result = Check(Secure);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Plain_connections_untrusted_certificates_and_open_http_are_refused()
    {
        var result = Check(new Dictionary<string, string?>(Secure)
        {
            ["Idm:Sso:Authority"] = "http://sso.bank.uz/master",
            ["ConnectionStrings:Idm"] = "Host=db;Database=idm;Username=idm;SSL Mode=Require",
            ["urls"] = "http://0.0.0.0:7100",
            ["Idm:Connectors:AD:Ldap:UseSsl"] = "false",
            ["Idm:Sso:AdminMfa:Required"] = "false",
        });

        Assert.Equal(4, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("Authority"));
        Assert.Contains(result.Errors, e => e.Contains("VerifyFull"));
        Assert.Contains(result.Errors, e => e.Contains("LDAPS"));
        Assert.Contains(result.Errors, e => e.Contains("TrustedProxies"));
        Assert.Contains(result.Warnings, w => w.Contains("AdminMfa"));

        // TLS'ni proksi tugatsa — HTTP ruxsat; manzil umuman berilmasa — standart HTTP, xato
        Assert.Empty(Check(new(Secure) { ["urls"] = "http://127.0.0.1:7100", ["Idm:TrustedProxies:0"] = "10.0.0.5" }).Errors);
        Assert.Contains(Check(new(Secure) { ["urls"] = null }).Errors, e => e.Contains("TrustedProxies"));
    }

    [Fact]
    public void Secrets_in_a_settings_file_are_refused_but_the_same_value_from_environment_is_fine()
    {
        var file = Path.Combine(Path.GetTempPath(), $"idm-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """{ "Idm": { "Connectors": { "AD": { "Ldap": { "WriteBindPassword": "fayldagi-sir" } } } } }""");
        try
        {
            var values = new Dictionary<string, string?>(Secure);
            var error = Assert.Single(Check(values, file).Errors);
            Assert.Contains("Idm:Connectors:AD:Ldap:WriteBindPassword", error);
            values["Idm:Connectors:AD:Ldap:WriteBindPassword"] = "muhitdagi-sir";   // ustun manba — fayl emas
            Assert.Empty(Check(values, file).Errors);
        }
        finally
        {
            File.Delete(file);
        }
    }
}

/// <summary>Butun server: Production'da xavfsiz bo'lmagan sozlama, sirlar katalogi va administrator uchun ikki bosqichli kirish.</summary>
public class HardeningApiTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private WebApplicationFactory<Program> Factory(string environment, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("ConnectionStrings:Idm", pg.ConnectionString);
            b.UseSetting("Idm:Sso:Authority", "https://sso.invalid/master");
            b.UseSetting("Idm:ProvisioningIntervalSeconds", "0");
            b.UseSetting("Idm:BootstrapAdmins:0", "boss");
            foreach (var (key, value) in settings) b.UseSetting(key, value);
            b.ConfigureTestServices(s => s.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        });

    [Fact]
    public void Production_with_a_plain_database_connection_does_not_start()
    {
        using var factory = Factory("Production");   // test bazasi SSL'siz
        var e = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("xavfsizlik xatosi", e.ToString());
    }

    [Fact]
    public async Task Secrets_directory_values_are_used()
    {
        var dir = Directory.CreateTempSubdirectory("idm-secrets").FullName;
        File.WriteAllText(Path.Combine(dir, "Idm__BootstrapAdmins__1"), "secret.admin");
        try
        {
            await using var factory = Factory("Testing", ("Idm:SecretsDirectory", dir));
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", "secret.admin");
            var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json, Ct);
            Assert.Contains("idm.admin", me!.Roles);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Admin_rights_need_a_second_factor_when_mfa_is_required()
    {
        await using var factory = Factory("Testing", ("Idm:Sso:AdminMfa:Required", "true"), ("Idm:Sso:AdminMfa:StepUpAcr", "idm-mfa"));
        HttpClient Client(string? amr)
        {
            var c = factory.CreateClient();
            c.DefaultRequestHeaders.Add("X-Test-User", "boss");
            if (amr is not null) c.DefaultRequestHeaders.Add("X-Test-Amr", amr);
            return c;
        }

        var passwordOnly = Client("pwd");
        Assert.Equal(HttpStatusCode.Forbidden, (await passwordOnly.GetAsync("/api/identities", Ct)).StatusCode);
        var me = await passwordOnly.GetFromJsonAsync<MeResponse>("/api/me", Json, Ct);
        Assert.Empty(me!.Roles);
        Assert.Equal(["idm.admin"], me.MfaRequiredFor);

        Assert.Equal("idm-mfa", me.MfaStepUpAcr);
        Assert.Equal(HttpStatusCode.OK, (await Client("pwd otp").GetAsync("/api/identities", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(null).GetAsync("/api/identities", Ct)).StatusCode);
    }
}
