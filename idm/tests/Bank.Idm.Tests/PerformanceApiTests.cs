using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Idm.Tests;

/// <summary>Yuklama testi topilmalari: JSON javoblar siqiladi, bosh sahifa hisoblari umumiy keshdan, sinxronlashdan keyin darhol yangilanadi.</summary>
public class PerformanceApiTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private WebApplicationFactory<Program> _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public PerformanceApiTests(PostgresFixture pg) => _pg = pg;

    public ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Idm", _pg.ConnectionString);
            b.UseSetting("Idm:Sso:Authority", "https://sso.invalid/master");
            b.UseSetting("Idm:ProvisioningIntervalSeconds", "0");
            b.UseSetting("Idm:BootstrapAdmins:0", "boss");
            b.UseSetting("Idm:DashboardCacheSeconds", "600");
            b.ConfigureTestServices(s => s.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-User", "boss");
        return c;
    }

    [Fact]
    public async Task Json_responses_are_compressed_when_the_client_accepts_it()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var compressed = await Admin().SendAsync(request, Ct);
        Assert.Equal("br", Assert.Single(compressed.Content.Headers.ContentEncoding));

        var plain = await Admin().GetAsync("/api/me", Ct);
        Assert.Empty(plain.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task Dashboard_counts_come_from_the_cache_until_a_sync_finishes()
    {
        var admin = Admin();
        var first = (await admin.GetFromJsonAsync<DashboardSummary>("/api/dashboard", Json, Ct))!;
        Assert.NotNull(first.CalculatedAt);

        await using (var db = _pg.CreateContext())
        {
            db.Identities.Add(new Identity { EmployeeNumber = "N1", FirstName = "Yangi", LastName = "Xodim", Source = IdentitySource.Manual });
            await db.SaveChangesAsync(Ct);
        }
        var cached = (await admin.GetFromJsonAsync<DashboardSummary>("/api/dashboard", Json, Ct))!;
        Assert.Equal((first.Identities, first.CalculatedAt), (cached.Identities, cached.CalculatedAt));

        // Sinxronlash tugadi — bosh sahifa darhol yangi raqamlarni ko'rsatadi
        await using (var db = _pg.CreateContext())
        {
            var app = new Application { Code = "HR", Name = "HR", Kind = ApplicationKind.HumanResources };
            db.AddRange(app, new ConnectorRun
            {
                Application = app, TriggeredBy = "system", Status = ConnectorRunStatus.Succeeded,
                StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow.AddSeconds(1)
            });
            await db.SaveChangesAsync(Ct);
        }
        var fresh = (await admin.GetFromJsonAsync<DashboardSummary>("/api/dashboard", Json, Ct))!;
        Assert.Equal(first.Identities + 1, fresh.Identities);
        Assert.True(fresh.CalculatedAt > first.CalculatedAt);
    }
}
