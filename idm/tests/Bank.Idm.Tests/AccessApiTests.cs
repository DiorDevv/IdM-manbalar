using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Idm.Tests;

public class AccessApiTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private WebApplicationFactory<Program> _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private Guid _credit, _approve, _karimov;

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using (var db = pg.CreateContext())
        {
            var ad = new Application { Code = "AD", Name = "AD", Kind = ApplicationKind.Directory };
            var credit = new Entitlement { Application = ad, ExternalId = "g1", Name = "kredit-berish" };
            var approve = new Entitlement { Application = ad, ExternalId = "g2", Name = "kredit-tasdiqlash" };
            var karimov = new Identity { EmployeeNumber = "T1", FirstName = "Aziz", LastName = "Karimov", Department = "Kredit bo'limi" };
            var account = new Account { Application = ad, Identity = karimov, ExternalId = "u1", Login = "a.karimov" };
            account.Entitlements.Add(new AccountEntitlement { Entitlement = credit });
            account.Entitlements.Add(new AccountEntitlement { Entitlement = approve });
            db.AddRange(ad, credit, approve, karimov, account);
            await db.SaveChangesAsync(Ct);
            (_credit, _approve, _karimov) = (credit.Id, approve.Id, karimov.Id);
        }
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Idm", pg.ConnectionString);
            b.UseSetting("Idm:Sso:Authority", "https://sso.invalid/master");
            b.UseSetting("Idm:BootstrapAdmins:0", "boss");
            b.ConfigureTestServices(s => s.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await pg.DisposeAsync();
    }

    private HttpClient Client(string user)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-User", user);
        return c;
    }

    [Fact]
    public async Task Admin_builds_a_role_with_a_birthright_rule_and_sees_access_and_sod()
    {
        var admin = Client("boss");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/roles", new SaveRoleRequest("bad code!", "X", null, RiskLevel.Low, true), Ct)).StatusCode);
        var created = await admin.PostAsJsonAsync("/api/roles", new SaveRoleRequest("KREDIT", "Kredit mutaxassisi", null, RiskLevel.Medium, true), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/roles", new SaveRoleRequest("KREDIT", "Takror", null, RiskLevel.Low, true), Ct)).StatusCode);
        var role = (await created.Content.ReadFromJsonAsync<RoleDetails>(Json, Ct))!;

        var withEntitlements = await admin.PutAsJsonAsync($"/api/roles/{role.Id}/entitlements", new SetRoleEntitlementsRequest([_credit]), Ct);
        Assert.Equal("kredit-berish", Assert.Single((await withEntitlements.Content.ReadFromJsonAsync<RoleDetails>(Json, Ct))!.Entitlements).Name);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/roles/{role.Id}/entitlements", new SetRoleEntitlementsRequest([Guid.NewGuid()]), Ct)).StatusCode);

        // Butun bankka rol beradigan bo'sh qoida taqiqlangan
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/roles/{role.Id}/rules", new AddRoleRuleRequest(" ", null), Ct)).StatusCode);
        var withRule = (await (await admin.PostAsJsonAsync($"/api/roles/{role.Id}/rules", new AddRoleRuleRequest("Kredit bo'limi", null), Ct))
            .Content.ReadFromJsonAsync<RoleDetails>(Json, Ct))!;
        Assert.Equal(1, Assert.Single(withRule.Rules).MatchCount);

        var result = await (await admin.PostAsync("/api/roles/recalculate", null, Ct)).Content.ReadFromJsonAsync<BirthrightResult>(Json, Ct);
        Assert.Equal(1, result!.Assigned);

        var sodBad = await admin.PostAsJsonAsync("/api/sod/rules", new SaveSodRuleRequest("SOD-1", "Kredit", null, RiskLevel.Critical, [_credit], [_credit]), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, sodBad.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/sod/rules",
            new SaveSodRuleRequest("SOD-1", "Kredit berish va tasdiqlash", null, RiskLevel.Critical, [_credit], [_approve]), Ct)).StatusCode);

        var access = await admin.GetFromJsonAsync<IdentityAccess>($"/api/identities/{_karimov}/access", Json, Ct);
        Assert.Equal("KREDIT", Assert.Single(access!.Roles).Code);
        Assert.Equal(AccessState.Granted, access.Lines.Single(l => l.EntitlementName == "kredit-berish").State);
        Assert.Equal(AccessState.Excess, access.Lines.Single(l => l.EntitlementName == "kredit-tasdiqlash").State);
        Assert.Equal("SOD-1", Assert.Single(access.Sod).RuleCode);
        Assert.Equal(1, (await admin.GetFromJsonAsync<DashboardSummary>("/api/dashboard", Json, Ct))!.SodViolations);
        var violations = await admin.GetFromJsonAsync<PagedResult<SodViolationItem>>("/api/sod/violations", Json, Ct);
        Assert.Equal("Karimov Aziz", Assert.Single(violations!.Items).IdentityName);

        // Rol berilgan: o'chirib bo'lmaydi (tarix saqlanadi)
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/roles/{role.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Non_admins_cannot_change_roles_or_sod_rules()
    {
        var employee = Client("a.karimov");
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync("/api/roles", new SaveRoleRequest("R1", "R1", null, RiskLevel.Low, true), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsync("/api/roles/recalculate", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/roles", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync("/api/sod/rules",
            new SaveSodRuleRequest("S", "S", null, RiskLevel.Low, [_credit], [_approve]), Ct)).StatusCode);
    }
}
