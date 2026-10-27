using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Siem;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Idm.Tests;

/// <summary>Sinov SIEM: TCP (ixtiyoriy TLS) qabul qiluvchi, RFC 6587 octet-counting ramkalarni yig'adi.</summary>
public sealed class FakeSiem : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2? _certificate;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    public List<string> Messages { get; } = [];
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public FakeSiem(X509Certificate2? certificate = null)
    {
        _certificate = certificate;
        _listener.Start();
        _loop = Task.Run(AcceptAsync);
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) when (_stop.IsCancellationRequested) { return; }   // to'xtatilgan tinglovchi (bekor qilish yoki yopilgan soket)
            using (client)
            {
                try
                {
                    Stream stream = client.GetStream();
                    if (_certificate is not null)
                    {
                        var ssl = new SslStream(stream);
                        await ssl.AuthenticateAsServerAsync(_certificate);
                        stream = ssl;
                    }
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer);
                    lock (Messages) Messages.AddRange(Frames(buffer.ToArray()));
                }
                catch (Exception e) when (e is IOException or System.Security.Authentication.AuthenticationException) { /* rad etilgan ulanish */ }
            }
        }
    }

    /// <summary>"&lt;uzunlik&gt; &lt;xabar&gt;" ramkalari.</summary>
    private static IEnumerable<string> Frames(byte[] data)
    {
        var i = 0;
        while (i < data.Length)
        {
            var space = Array.IndexOf(data, (byte)' ', i);
            var length = int.Parse(Encoding.ASCII.GetString(data, i, space - i));
            yield return Encoding.UTF8.GetString(data, space + 1, length);
            i = space + 1 + length;
        }
    }

    public async Task<List<string>> WaitAsync(int count)
    {
        for (var i = 0; i < 100; i++)
        {
            lock (Messages) if (Messages.Count >= count) return [.. Messages];
            await Task.Delay(50);
        }
        lock (Messages) return [.. Messages];
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _loop;
    }
}

public class SiemTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly MutableTime _time = new(new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero));

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await AuditAsync(new AuditEntry("boss", "Boss \"katta\"", "identity.created", "identity", "1", """{"employeeNumber":"T1"}"""),
            new AuditEntry("system", null, "account.disabled", "account", "2", null));
    }

    public async ValueTask DisposeAsync() => await pg.DisposeAsync();

    private async Task AuditAsync(params AuditEntry[] entries)
    {
        await using var db = pg.CreateContext();
        await new AuditLog(db, _time).SaveChangesAsync(entries, Ct);
    }

    private async Task<ForwardResult> ForwardAsync(SiemOptions options)
    {
        await using var db = pg.CreateContext();
        return await new SiemForwarder(db, _time).ForwardAsync(options, Ct);
    }

    [Fact]
    public async Task Audit_is_sent_as_rfc5424_with_json_and_hash_and_resumes_from_the_checkpoint()
    {
        await using var siem = new FakeSiem();
        var options = new SiemOptions { Enabled = true, Host = "127.0.0.1", Port = siem.Port, Protocol = SiemProtocols.Tcp };

        Assert.Equal(2, (await ForwardAsync(options)).Sent);
        var messages = await siem.WaitAsync(2);
        Assert.Equal(2, messages.Count);
        Assert.Matches(@"^<109>1 \d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{6}Z \S+ bank-idm - identity\.created \[idm@32473 id=""\d+"" actor=""boss"" hash=""[0-9a-f]{64}""\] \{", messages[0]);
        var json = JsonDocument.Parse(messages[0][messages[0].IndexOf('{')..]).RootElement;
        Assert.Equal(("identity.created", "Boss \"katta\"", "{\"employeeNumber\":\"T1\"}"),
            (json.GetProperty("action").GetString(), json.GetProperty("actorName").GetString(), json.GetProperty("details").GetString()));
        Assert.Equal("0000000000000000000000000000000000000000000000000000000000000000", json.GetProperty("previousHash").GetString());

        // Faqat yangi yozuvlar
        Assert.Equal(0, (await ForwardAsync(options)).Sent);
        await AuditAsync(new AuditEntry("boss", null, "role.created", "role", "3"));
        var third = await ForwardAsync(options);
        Assert.Equal((1, 0L), (third.Sent, third.Lag));
        Assert.Contains("role.created", (await siem.WaitAsync(3))[2]);
    }

    [Fact]
    public async Task Unreachable_siem_keeps_the_checkpoint_and_records_the_error()
    {
        // Bog'langan, lekin tinglamaydigan port: ulanish rad etiladi va port boshqa testga berilmaydi
        using var closed = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        closed.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var options = new SiemOptions { Enabled = true, Host = "127.0.0.1", Port = ((IPEndPoint)closed.LocalEndPoint!).Port, Protocol = SiemProtocols.Tcp, TimeoutSeconds = 3 };

        await Assert.ThrowsAsync<IOException>(() => ForwardAsync(options));

        await using var db = pg.CreateContext();
        var checkpoint = await db.SiemCheckpoints.SingleAsync(Ct);
        Assert.Equal(0, checkpoint.LastEventId);
        Assert.NotNull(checkpoint.LastError);
        await using var siem = new FakeSiem();
        Assert.Equal(2, (await ForwardAsync(options.WithPort(siem.Port))).Sent);   // tiklanganda — boshidan, yo'qotishsiz
    }

    [Fact]
    public async Task Tls_trusts_only_the_bank_ca_given_in_the_settings()
    {
        using var ca = CreateCa("Bank Root CA");
        using var server = CreateServerCertificate(ca, "localhost");
        var caPath = Path.Combine(Path.GetTempPath(), $"idm-ca-{Guid.NewGuid():N}.pem");
        File.WriteAllText(caPath, ca.ExportCertificatePem());
        using var otherCa = CreateCa("Begona CA");
        var otherPath = Path.Combine(Path.GetTempPath(), $"idm-ca-{Guid.NewGuid():N}.pem");
        File.WriteAllText(otherPath, otherCa.ExportCertificatePem());
        try
        {
            await using var siem = new FakeSiem(server);
            var options = new SiemOptions { Enabled = true, Host = "localhost", Port = siem.Port, CaCertificatePath = otherPath, TimeoutSeconds = 5 };
            await Assert.ThrowsAsync<IOException>(() => ForwardAsync(options));   // begona CA imzolagan sertifikat
            options.CaCertificatePath = caPath;
            Assert.Equal(2, (await ForwardAsync(options)).Sent);
            Assert.Equal(2, (await siem.WaitAsync(2)).Count);
        }
        finally
        {
            File.Delete(caPath);
            File.Delete(otherPath);
        }
    }

    private static X509Certificate2 CreateCa(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static X509Certificate2 CreateServerCertificate(X509Certificate2 ca, string host)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var signed = request.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), RandomNumberGenerator.GetBytes(8));
        // Linux'da SslStream server sertifikatining kaliti eksport qilinadigan PFX'dan yuklanishi kerak
        return X509CertificateLoader.LoadPkcs12(signed.CopyWithPrivateKey(key).Export(X509ContentType.Pkcs12), null);
    }

    [Fact]
    public async Task Metrics_need_their_own_token_and_contain_no_personal_data()
    {
        const string token = "metrics-token-0123456789abcdef0123456789";
        WebApplicationFactory<Program> Factory(string? metricsToken) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Idm", pg.ConnectionString);
            b.UseSetting("Idm:Sso:Authority", "https://sso.invalid/master");
            b.UseSetting("Idm:ProvisioningIntervalSeconds", "0");
            if (metricsToken is not null) b.UseSetting("Idm:MetricsToken", metricsToken);
            b.ConfigureTestServices(s => s.AddAuthentication(o => o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        });

        await using (var off = Factory(null))
            Assert.Equal(HttpStatusCode.NotFound, (await off.CreateClient().GetAsync("/metrics", Ct)).StatusCode);

        await using var on = Factory(token);
        var client = on.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics", Ct)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token + "x");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics", Ct)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var text = await client.GetStringAsync("/metrics", Ct);
        Assert.Contains("idm_up 1", text);
        Assert.Contains("idm_provisioning_operations{status=\"Proposed\"} 0", text);
        Assert.Contains("idm_leavers_with_active_accounts 0", text);
        Assert.Matches(@"idm_audit_last_event_id \d+", text);
        Assert.DoesNotContain("boss", text);
    }
}

file static class SiemOptionsExtensions
{
    public static SiemOptions WithPort(this SiemOptions o, int port) => new()
    {
        Enabled = o.Enabled, Host = o.Host, Port = port, Protocol = o.Protocol, TimeoutSeconds = o.TimeoutSeconds, CaCertificatePath = o.CaCertificatePath
    };
}
