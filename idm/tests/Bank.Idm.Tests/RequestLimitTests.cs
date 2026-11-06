using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Idm.Tests;

/// <summary>So'rovlar chegarasi: bitta foydalanuvchi yoki skript serverni band qila olmaydi, boshqalar ishlashda davom etadi.</summary>
public class RequestLimitTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private WebApplicationFactory<Program> _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RequestLimitTests(PostgresFixture pg) => _pg = pg;

    public ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Idm", _pg.ConnectionString);
            b.UseSetting("Idm:Sso:Authority", "https://sso.invalid/master");
            b.UseSetting("Idm:ProvisioningIntervalSeconds", "0");
            b.UseSetting("Idm:BootstrapAdmins:0", "boss");
            b.UseSetting("Idm:MetricsToken", "metrics-secret");
            // Kichik chegaralar: sekundiga 1 ta token, birdaniga 5 ta
            b.UseSetting("Idm:Limits:UserBurst", "5");
            b.UseSetting("Idm:Limits:UserPerSecond", "1");
            b.UseSetting("Idm:Limits:AnonymousPerMinute", "3");
            b.ConfigureTestServices(s => s.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Client(string? user)
    {
        var c = _factory.CreateClient();
        if (user is not null) c.DefaultRequestHeaders.Add("X-Test-User", user);
        return c;
    }

    [Fact]
    public async Task User_over_the_limit_gets_429_with_retry_after_and_other_users_are_not_affected()
    {
        var boss = Client("boss");
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++) codes.Add((await boss.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(5, codes.Count(c => c == HttpStatusCode.OK));
        Assert.Equal(3, codes.Count(c => c == HttpStatusCode.TooManyRequests));

        var rejected = await boss.GetAsync("/api/me", Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(int.Parse(rejected.Headers.GetValues("Retry-After").Single()) >= 1);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.Contains("qayta urinib", await rejected.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, (await Client("a.karimov").GetAsync("/api/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_limited_by_ip_but_health_and_metrics_are_not()
    {
        var anonymous = Client(null);
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) codes.Add((await anonymous.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(3, codes.Count(c => c == HttpStatusCode.Unauthorized));
        Assert.Equal(2, codes.Count(c => c == HttpStatusCode.TooManyRequests));

        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health", Ct)).StatusCode);

        var metrics = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        metrics.Headers.Authorization = new("Bearer", "metrics-secret");
        var text = await (await anonymous.SendAsync(metrics, Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("idm_http_throttled_total{reason=\"anonymous\"} 2", text);
    }

    [Fact]
    public async Task Heavy_endpoints_are_marked_and_still_work_within_limits()
    {
        var dataSource = _factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>();
        var heavy = dataSource.Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<Bank.Idm.Api.Security.HeavyRequestAttribute>() is not null)
            .Select(e => e.RoutePattern.RawText).ToList();
        Assert.Contains("/api/reports/export/{key}", heavy);
        Assert.Contains("/api/audit/verify", heavy);
        Assert.Contains("/api/roles/recalculate", heavy);
        Assert.DoesNotContain("/api/me", heavy);

        Assert.Equal(HttpStatusCode.OK, (await Client("boss").GetAsync("/api/audit/verify", Ct)).StatusCode);
    }
}
