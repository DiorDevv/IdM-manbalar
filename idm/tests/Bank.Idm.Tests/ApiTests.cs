using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Tests;

/// <summary>Testda SSO o'rniga: X-Test-User sarlavhasidagi login, X-Test-Scope sarlavhasidagi ruxsat doirasi.</summary>
public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    public const string SchemeName = "Test";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("sub", user!), new("name", $"{user} (test)") };
        claims.Add(new("scope", Request.Headers.TryGetValue("X-Test-Scope", out var scope) ? scope.ToString() : "openid idm"));
        // SSO'dan kelgan "idm.admin" roli e'tiborga olinmasligi kerak (vakolat faqat IdM bazasidan)
        if (Request.Headers.ContainsKey("X-Test-Forged-Role")) claims.Add(new(ClaimTypes.Role, PortalRoles.Admin));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, "sub", ClaimTypes.Role)), SchemeName)));
    }
}

public class ApiTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private WebApplicationFactory<Program> _factory = null!;

    public ApiTests(PostgresFixture pg) => _pg = pg;

    public ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Idm", _pg.ConnectionString);
            b.UseSetting("Idm:Sso:Authority", "https://sso.invalid/master");
            b.UseSetting("Idm:BootstrapAdmins:0", "boss");
            b.ConfigureTestServices(s => s.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Client(string? user, string? scope = null, bool forgedRole = false)
    {
        var c = _factory.CreateClient();
        if (user is not null) c.DefaultRequestHeaders.Add("X-Test-User", user);
        if (scope is not null) c.DefaultRequestHeaders.Add("X-Test-Scope", scope);
        if (forgedRole) c.DefaultRequestHeaders.Add("X-Test-Forged-Role", "1");
        return c;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    // API enum'larni matn sifatida qaytaradi ("Active"), raqam emas
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Health_is_public_and_returns_security_headers()
    {
        var r = await Client(null).GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(r.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Api_requires_authentication_and_the_idm_scope()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("boss", scope: "openid profile").GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client("boss").GetAsync("/api/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Bootstrap_admin_gets_the_admin_role_from_the_database()
    {
        var me = await Client("boss").GetFromJsonAsync<MeResponse>("/api/me", Json, Ct);
        Assert.Contains(PortalRoles.Admin, me!.Roles);
    }

    [Fact]
    public async Task Ordinary_employee_cannot_read_identities_even_with_a_forged_sso_role()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("a.karimov").GetAsync("/api/identities", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("a.karimov", forgedRole: true).GetAsync("/api/identities", Ct)).StatusCode);
        var me = await Client("a.karimov", forgedRole: true).GetFromJsonAsync<MeResponse>("/api/me", Json, Ct);
        Assert.Empty(me!.Roles);
    }

    [Fact]
    public async Task Admin_creates_an_identity_and_the_change_is_audited()
    {
        var admin = Client("boss");
        var number = $"TAB-{Guid.NewGuid():N}"[..14];
        var created = await admin.PostAsJsonAsync("/api/identities", new CreateIdentityRequest(number, "Aziz", "Karimov", null, "a.karimov@bank.test", "Kredit bo'limi", "Kredit mutaxassisi", null), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/api/identities", new CreateIdentityRequest(number, "X", "Y", null, null, null, null, null), Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var invalid = await admin.PostAsJsonAsync("/api/identities", new CreateIdentityRequest("", "", "", null, "not-an-email", null, null, null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var list = await admin.GetFromJsonAsync<PagedResult<IdentityListItem>>($"/api/identities?search={number}", Json, Ct);
        var item = Assert.Single(list!.Items);
        Assert.Equal("Karimov Aziz", item.DisplayName);

        await using var db = _pg.CreateContext();
        var evt = await db.AuditEvents.SingleAsync(e => e.Action == "identity.created" && e.TargetId == item.Id.ToString(), Ct);
        Assert.Equal("boss", evt.ActorId);
        Assert.Contains(number, evt.Details);
    }

    [Fact]
    public async Task Employee_without_admin_role_cannot_create_identities()
    {
        var r = await Client("a.karimov").PostAsJsonAsync("/api/identities", new CreateIdentityRequest("TAB-X", "A", "B", null, null, null, null, null), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
