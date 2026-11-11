using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Idm.Tests;

/// <summary>
/// Ikki kishilik tasdiq: xavfli administrator amali so'rovga aylanadi va boshqa administrator tasdiqlagandagina qo'llanadi.
/// boss va boss2 — administratorlar, a.karimov — oddiy xodim.
/// </summary>
public class FourEyesTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private WebApplicationFactory<Program> _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private Guid _credit, _approve, _role, _karimov;

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using (var db = pg.CreateContext())
        {
            var ad = new Application { Code = "AD", Name = "AD", Kind = ApplicationKind.Directory };
            var credit = new Entitlement { Application = ad, ExternalId = "g1", Name = "kredit-berish" };
            var approve = new Entitlement { Application = ad, ExternalId = "g2", Name = "kredit-tasdiqlash" };
            var karimov = new Identity { EmployeeNumber = "T1", FirstName = "Aziz", LastName = "Karimov", Department = "Kredit" };
            var role = new Role { Code = "KREDIT", Name = "Kredit" };
            db.AddRange(ad, credit, approve, karimov, role, new Account { Application = ad, Identity = karimov, ExternalId = "u1", Login = "a.karimov" });
            await db.SaveChangesAsync(Ct);
            (_credit, _approve, _role, _karimov) = (credit.Id, approve.Id, role.Id, karimov.Id);
        }
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Idm", pg.ConnectionString);
            b.UseSetting("Idm:Sso:Authority", "https://sso.invalid/master");
            b.UseSetting("Idm:ProvisioningIntervalSeconds", "0");
            b.UseSetting("Idm:BootstrapAdmins:0", "boss");
            b.UseSetting("Idm:BootstrapAdmins:1", "boss2");
            b.ConfigureTestServices(s => s.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        });
        _ = _factory.Server;   // ishga tushirish: administratorlar bazaga yoziladi
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await pg.DisposeAsync();
    }

    private HttpClient As(string login)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-User", login);
        return c;
    }

    private static async Task<ChangeRequestItem> PendingAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChangePending>(Json, Ct))!.ChangeRequest;
    }

    private Task<HttpResponseMessage> Approve(string login, Guid id, string? comment = null) =>
        As(login).PostAsJsonAsync($"/api/change-requests/{id}/approve", new ChangeDecisionBody(comment), Ct);

    private Task<HttpResponseMessage> AddRule(string login, string? department) =>
        As(login).PostAsJsonAsync($"/api/roles/{_role}/rules", new AddRoleRuleRequest(department, null), Ct);

    [Fact]
    public async Task A_risky_change_waits_for_a_second_admin_and_is_applied_on_their_approval()
    {
        var change = await PendingAsync(await AddRule("boss", "Kredit"));
        Assert.Equal(("Pending", "role.rule-add"), (change.Status, change.Kind));
        Assert.Contains(change.Preview, p => p.Action == "role.rule-added" && p.Details!.Contains("Kredit"));
        await using (var db = pg.CreateContext())
        {
            Assert.False(await db.RoleRules.AnyAsync(Ct));                                        // hali qo'llanmagan
            Assert.False(await db.AuditEvents.AnyAsync(e => e.Action == "role.rule-added", Ct));    // sinov yozuvlari bekor qilingan
        }
        Assert.Equal(1, (await As("boss2").GetFromJsonAsync<MeResponse>("/api/me", Json, Ct))!.PendingChanges);
        Assert.Equal(0, (await As("boss").GetFromJsonAsync<MeResponse>("/api/me", Json, Ct))!.PendingChanges);

        Assert.Equal(HttpStatusCode.Forbidden, (await Approve("boss", change.Id)).StatusCode);       // o'zi o'zini tasdiqlamaydi
        Assert.Equal(HttpStatusCode.Forbidden, (await Approve("a.karimov", change.Id)).StatusCode);  // administrator emas
        var approved = await Approve("boss2", change.Id, "Tekshirildi");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Single((await approved.Content.ReadFromJsonAsync<RoleDetails>(Json, Ct))!.Rules);
        Assert.Equal(HttpStatusCode.Conflict, (await Approve("boss2", change.Id)).StatusCode);       // bir marta

        await using (var db = pg.CreateContext())
        {
            var rule = await db.RoleRules.SingleAsync(Ct);
            Assert.Equal(("Kredit", "boss"), (rule.Department, rule.CreatedBy));
            Assert.Equal("boss", (await db.AuditEvents.SingleAsync(e => e.Action == "role.rule-added", Ct)).ActorId);
            Assert.Equal("boss2", (await db.AuditEvents.SingleAsync(e => e.Action == "change.approved", Ct)).ActorId);
            var r = await db.ChangeRequests.SingleAsync(Ct);
            Assert.Equal((ChangeRequestStatus.Applied, "boss2", "Tekshirildi"), (r.Status, r.DecidedBy, r.DecisionComment));
            Assert.True((await new AuditLog(db, TimeProvider.System).VerifyChainAsync(Ct)).IsValid);
        }
    }

    [Fact]
    public async Task Invalid_changes_fail_immediately_and_no_request_is_created()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await AddRule("boss", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As("boss").DeleteAsync($"/api/roles/{Guid.NewGuid()}", Ct)).StatusCode);
        await using var db = pg.CreateContext();
        Assert.False(await db.ChangeRequests.AnyAsync(Ct));
    }

    [Fact]
    public async Task Reject_needs_a_reason_cancel_is_only_for_the_requester_and_expired_requests_cannot_be_approved()
    {
        var a = await PendingAsync(await AddRule("boss", "Kredit"));
        Assert.Equal(HttpStatusCode.BadRequest, (await As("boss2").PostAsJsonAsync($"/api/change-requests/{a.Id}/reject", new ChangeDecisionBody(""), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As("boss").PostAsJsonAsync($"/api/change-requests/{a.Id}/reject", new ChangeDecisionBody("o'zimniki"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As("boss2").PostAsJsonAsync($"/api/change-requests/{a.Id}/reject", new ChangeDecisionBody("Bo'lim nomi noto'g'ri"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Approve("boss2", a.Id)).StatusCode);

        var b = await PendingAsync(await AddRule("boss", "IT"));
        Assert.Equal(HttpStatusCode.Forbidden, (await As("boss2").PostAsync($"/api/change-requests/{b.Id}/cancel", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As("boss").PostAsync($"/api/change-requests/{b.Id}/cancel", null, Ct)).StatusCode);

        var c = await PendingAsync(await AddRule("boss", "Kassa"));
        await using (var db = pg.CreateContext())
            await db.ChangeRequests.Where(r => r.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
        Assert.Equal(HttpStatusCode.Conflict, (await Approve("boss2", c.Id)).StatusCode);

        var list = await As("boss2").GetFromJsonAsync<PagedResult<ChangeRequestItem>>("/api/change-requests", Json, Ct);
        Assert.Equal(["Expired", "Cancelled", "Rejected"], list!.Items.Select(i => i.Status));
        Assert.Empty((await As("boss2").GetFromJsonAsync<PagedResult<ChangeRequestItem>>("/api/change-requests?status=pending", Json, Ct))!.Items);
        await using (var db = pg.CreateContext())
            Assert.False(await db.RoleRules.AnyAsync(Ct));
    }

    [Fact]
    public async Task If_the_world_changed_before_approval_nothing_is_applied_and_the_request_fails()
    {
        var change = await PendingAsync(await As("boss").PutAsJsonAsync($"/api/roles/{_role}/entitlements", new SetRoleEntitlementsRequest([_credit, _approve]), Ct));
        await using (var db = pg.CreateContext())
            await db.Entitlements.Where(e => e.Id == _approve).ExecuteUpdateAsync(s => s.SetProperty(e => e.DeletedAt, DateTimeOffset.UtcNow), Ct);

        var response = await Approve("boss2", change.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var check = pg.CreateContext();
        Assert.False(await check.RoleEntitlements.AnyAsync(Ct));
        var r = await check.ChangeRequests.SingleAsync(Ct);
        Assert.Equal(ChangeRequestStatus.Failed, r.Status);
        Assert.Contains("o'chirilgan", r.Error);
        Assert.False(await check.AuditEvents.AnyAsync(e => e.Action == "change.approved" || e.Action == "role.entitlements-changed", Ct));
    }

    [Fact]
    public async Task Portal_roles_are_managed_only_through_two_admins_and_never_for_oneself()
    {
        // Oddiy xodimga auditor vakolati: tasdiqlangandan keyin u hamma narsani ko'radi
        Assert.Equal(HttpStatusCode.Forbidden, (await As("a.karimov").GetAsync("/api/identities", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As("a.karimov").PostAsJsonAsync("/api/portal-roles", new PortalRoleBody("a.karimov", PortalRole.Admin), Ct)).StatusCode);
        var grant = await PendingAsync(await As("boss").PostAsJsonAsync("/api/portal-roles", new PortalRoleBody("a.karimov", PortalRole.Auditor), Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await As("a.karimov").GetAsync("/api/identities", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Approve("boss2", grant.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As("a.karimov").GetAsync("/api/identities", Ct)).StatusCode);
        var roles = await As("boss").GetFromJsonAsync<List<PortalRoleItem>>("/api/portal-roles", Json, Ct);
        Assert.Equal("Karimov Aziz", roles!.Single(r => r.Login == "a.karimov").Name);

        // O'ziga tegishli vakolat o'zgarishini tasdiqlab bo'lmaydi
        var self = await PendingAsync(await As("boss").PostAsJsonAsync("/api/portal-roles", new PortalRoleBody("boss2", PortalRole.Auditor), Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await Approve("boss2", self.Id)).StatusCode);

        // Ikkitadan kam administrator qolmaydi (aks holda keyingi o'zgarishlarni tasdiqlaydigan odam yo'q)
        Assert.Equal(HttpStatusCode.Conflict, (await As("boss").PostAsJsonAsync("/api/portal-roles/revoke", new PortalRoleBody("boss2", PortalRole.Admin), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As("boss").PostAsJsonAsync("/api/portal-roles", new PortalRoleBody("bad login!", PortalRole.Auditor), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_requester_who_lost_admin_rights_cannot_get_their_change_applied()
    {
        var change = await PendingAsync(await AddRule("boss", "Kredit"));
        await using (var db = pg.CreateContext())
            await db.PortalRoleAssignments.Where(p => p.Login == "boss").ExecuteDeleteAsync(Ct);
        Assert.Equal(HttpStatusCode.Conflict, (await Approve("boss2", change.Id)).StatusCode);
        await using var check = pg.CreateContext();
        Assert.Equal(ChangeRequestStatus.Failed, (await check.ChangeRequests.SingleAsync(Ct)).Status);
        Assert.False(await check.RoleRules.AnyAsync(Ct));
    }

    [Fact]
    public async Task SoD_exception_cannot_be_approved_by_the_employee_it_is_for()
    {
        Guid rule;
        await using (var db = pg.CreateContext())
        {
            var r = new SodRule { Code = "SOD-1", Name = "Kredit", CreatedBy = "boss" };
            r.Entitlements.Add(new SodRuleEntitlement { EntitlementId = _credit, Side = SodSide.Left });
            r.Entitlements.Add(new SodRuleEntitlement { EntitlementId = _approve, Side = SodSide.Right });
            db.SodRules.Add(r);
            // a.karimov ham administrator: unga istisno so'raladi
            db.PortalRoleAssignments.Add(new PortalRoleAssignment { Login = "a.karimov", Role = PortalRole.Admin, CreatedBy = "test" });
            await db.SaveChangesAsync(Ct);
            rule = r.Id;
        }
        var until = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var change = await PendingAsync(await As("boss").PostAsJsonAsync("/api/sod/exceptions",
            new SodExceptionBody(rule, _karimov, "Kichik filialda xodim yetishmaydi", "Har oy bosh ofis tekshiradi", until), Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await Approve("a.karimov", change.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Approve("boss2", change.Id)).StatusCode);
        await using var check = pg.CreateContext();
        Assert.Equal("boss", (await check.SodExceptions.SingleAsync(Ct)).CreatedBy);
    }
}
