using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Bank.Idm.Infrastructure.Requests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Idm.Tests;

/// <summary>
/// Ariza va tasdiqlash. Bank tuzilmasi: Rahimova — kredit bo'limi boshlig'i, Karimov — uning xodimi (ABS operatori ham),
/// Aliyev — "Kredit" rolining egasi, Yusupova — boshqa bo'lim, boss — IdM administratori (xodim yozuvi yo'q).
/// </summary>
public class RequestTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private WebApplicationFactory<Program> _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private Guid _karimov, _rahimova, _yusupova, _kredit, _hidden;

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using (var db = pg.CreateContext())
        {
            var ad = new Application { Code = "AD", Name = "AD", Kind = ApplicationKind.Directory };
            var credit = new Entitlement { Application = ad, ExternalId = "g1", Name = "kredit-berish" };
            var abs = new Entitlement { Application = ad, ExternalId = "g2", Name = "abs-operatorlar" };
            var rahimova = new Identity { EmployeeNumber = "T2", FirstName = "Dilnoza", LastName = "Rahimova", Department = "Kredit" };
            var karimov = new Identity { EmployeeNumber = "T1", FirstName = "Aziz", LastName = "Karimov", Department = "Kredit", Manager = rahimova };
            var aliyev = new Identity { EmployeeNumber = "T3", FirstName = "Jasur", LastName = "Aliyev" };
            var yusupova = new Identity { EmployeeNumber = "T4", FirstName = "Nodira", LastName = "Yusupova" };
            Account Acc(Identity i, string login) => new() { Application = ad, Identity = i, ExternalId = login, Login = login };
            var karimovAcc = Acc(karimov, "a.karimov");
            karimovAcc.Entitlements.Add(new AccountEntitlement { Entitlement = abs });
            var kredit = new Role { Code = "KREDIT", Name = "Kredit mutaxassisi", Owner = aliyev };
            kredit.Entitlements.Add(new RoleEntitlement { Entitlement = credit });
            var hidden = new Role { Code = "ADMIN", Name = "Yashirin", IsRequestable = false };
            var sod = new SodRule { Code = "SOD-1", Name = "Kredit va ABS", CreatedBy = "test" };
            sod.Entitlements.Add(new SodRuleEntitlement { Entitlement = credit, Side = SodSide.Left });
            sod.Entitlements.Add(new SodRuleEntitlement { Entitlement = abs, Side = SodSide.Right });
            db.AddRange(ad, credit, abs, rahimova, karimov, aliyev, yusupova, karimovAcc, Acc(rahimova, "d.rahimova"), Acc(aliyev, "j.aliyev"),
                Acc(yusupova, "n.yusupova"), kredit, hidden, sod);
            await db.SaveChangesAsync(Ct);
            (_karimov, _rahimova, _yusupova, _kredit, _hidden) = (karimov.Id, rahimova.Id, yusupova.Id, kredit.Id, hidden.Id);
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

    private HttpClient As(string login)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-User", login);
        return c;
    }

    private async Task<RequestDetails> CreateAsync(string login, Guid roleId, Guid? beneficiary = null)
    {
        var r = await As(login).PostAsJsonAsync("/api/requests", new CreateRequestBody(beneficiary, roleId, "Kredit arizalarini ko'rib chiqish uchun kerak", null), Ct);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<RequestDetails>(Json, Ct))!;
    }

    private static async Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid id, bool approve, string? comment = null) =>
        await client.PostAsJsonAsync($"/api/requests/{id}/decision", new DecisionBody(approve, comment), Ct);

    [Fact]
    public async Task Request_goes_through_manager_owner_and_security_then_the_role_is_granted()
    {
        var preview = await (await As("a.karimov").PostAsJsonAsync("/api/requests/preview", new PreviewRequestBody(null, _kredit), Ct))
            .Content.ReadFromJsonAsync<RequestPreview>(Json, Ct);
        Assert.True(preview!.CanSubmit);
        Assert.Equal([ApprovalStepKind.Manager, ApprovalStepKind.ResourceOwner, ApprovalStepKind.Security], preview.Steps.Select(s => s.Kind));
        Assert.Equal("Rahimova Dilnoza", preview.Steps[0].ApproverName);
        Assert.Equal("SOD-1", Assert.Single(preview.Sod).RuleCode);   // Karimov ABS operatori: kredit berish bilan ziddiyat

        var request = await CreateAsync("a.karimov", _kredit);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);

        // O'zi, boshqa bo'lim xodimi va navbati kelmagan rol egasi tasdiqlay olmaydi; begona xodim arizani ko'ra olmaydi
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(As("a.karimov"), request.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(As("n.yusupova"), request.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As("n.yusupova").GetAsync($"/api/requests/{request.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(As("j.aliyev"), request.Id, true)).StatusCode);

        Assert.Equal(1, (await As("d.rahimova").GetFromJsonAsync<MeResponse>("/api/me", Json, Ct))!.InboxCount);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(As("d.rahimova"), request.Id, true, "Tasdiqlayman")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(As("j.aliyev"), request.Id, true)).StatusCode);
        var inbox = await As("boss").GetFromJsonAsync<PagedResult<RequestListItem>>("/api/requests?scope=inbox", Json, Ct);
        Assert.Equal(ApprovalStepKind.Security, Assert.Single(inbox!.Items).CurrentStep);
        var final = await (await DecideAsync(As("boss"), request.Id, true, "Kompensatsion nazorat: oylik tekshiruv")).Content.ReadFromJsonAsync<RequestDetails>(Json, Ct);

        Assert.Equal(AccessRequestStatus.Approved, final!.Status);
        Assert.All(final.Approvals, a => Assert.Equal(ApprovalDecision.Approved, a.Decision));
        var access = await As("boss").GetFromJsonAsync<IdentityAccess>($"/api/identities/{_karimov}/access", Json, Ct);
        var role = Assert.Single(access!.Roles);
        Assert.Equal(("KREDIT", RoleAssignmentSource.Request), (role.Code, role.Source));
        Assert.Equal(AccessState.Missing, access.Lines.Single(l => l.EntitlementName == "kredit-berish").State);   // tizimda G bosqichida beriladi
        await using var db = pg.CreateContext();
        var actions = await db.AuditEvents.Where(e => e.TargetId == request.Id.ToString()).Select(e => e.Action).ToListAsync(Ct);
        Assert.Equal(["request.created", "request.step-approved", "request.step-approved", "request.step-approved", "request.approved"], actions);
        Assert.True((await new AuditLog(db, TimeProvider.System).VerifyChainAsync(Ct)).IsValid);
    }

    [Fact]
    public async Task Rejection_needs_a_reason_and_duplicates_or_invalid_requests_are_refused()
    {
        var request = await CreateAsync("a.karimov", _kredit);
        var karimov = As("a.karimov");
        Assert.Equal(HttpStatusCode.Conflict, (await karimov.PostAsJsonAsync("/api/requests", new CreateRequestBody(null, _kredit, "Takroriy ariza, takroriy ariza", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await karimov.PostAsJsonAsync("/api/requests", new CreateRequestBody(null, _hidden, "So'raladigan rol emas", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await karimov.PostAsJsonAsync("/api/requests", new CreateRequestBody(null, _kredit, "qisqa", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await karimov.PostAsJsonAsync("/api/requests",
            new CreateRequestBody(null, _kredit, "Juda uzoq muddatga kerak bo'ldi", DateTimeOffset.UtcNow.AddYears(2)), Ct)).StatusCode);
        // Boshqa bo'lim xodimi uchun so'rab bo'lmaydi; xodim yozuvi yo'q foydalanuvchi ariza bera olmaydi
        Assert.Equal(HttpStatusCode.Forbidden, (await As("n.yusupova").PostAsJsonAsync("/api/requests",
            new CreateRequestBody(_karimov, _kredit, "Hamkasbim uchun so'rayapman", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As("boss").PostAsJsonAsync("/api/requests",
            new CreateRequestBody(null, _kredit, "Administrator o'zi uchun", null), Ct)).StatusCode);

        var rahimova = As("d.rahimova");
        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(rahimova, request.Id, false)).StatusCode);
        var rejected = await (await DecideAsync(rahimova, request.Id, false, "Bo'limda bu vazifa yo'q")).Content.ReadFromJsonAsync<RequestDetails>(Json, Ct);
        Assert.Equal(AccessRequestStatus.Rejected, rejected!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(As("j.aliyev"), request.Id, true)).StatusCode);
    }

    [Fact]
    public async Task Manager_requesting_for_a_subordinate_skips_their_own_step_and_requester_can_cancel()
    {
        var request = await CreateAsync("d.rahimova", _kredit, _karimov);
        var manager = request.Approvals.Single(a => a.Kind == ApprovalStepKind.Manager);
        Assert.Equal((ApprovalDecision.Approved, "system"), (manager.Decision, manager.DecidedBy));
        Assert.True(request.CanCancel);

        Assert.Equal(HttpStatusCode.Forbidden, (await As("a.karimov").PostAsync($"/api/requests/{request.Id}/cancel", null, Ct)).StatusCode);
        var cancelled = await (await As("d.rahimova").PostAsync($"/api/requests/{request.Id}/cancel", null, Ct)).Content.ReadFromJsonAsync<RequestDetails>(Json, Ct);
        Assert.Equal(AccessRequestStatus.Cancelled, cancelled!.Status);
        var mine = await As("a.karimov").GetFromJsonAsync<PagedResult<RequestListItem>>("/api/requests", Json, Ct);
        Assert.Single(mine!.Items);   // xodim o'zi uchun yuborilgan arizani ham ko'radi
        Assert.Equal(HttpStatusCode.Forbidden, (await As("a.karimov").GetAsync("/api/requests?scope=all", Ct)).StatusCode);
    }

    [Fact]
    public async Task Two_approvers_deciding_at_the_same_time_cannot_both_win()
    {
        var request = await CreateAsync("a.karimov", _kredit);
        await using var db1 = pg.CreateContext();
        await using var db2 = pg.CreateContext();
        AccessRequestService Service(IdmDbContext db) => new(db, new AuditLog(db, TimeProvider.System), new AccessAnalyzer(db, TimeProvider.System), TimeProvider.System);
        var rahimova = new RequestActor("d.rahimova", null, _rahimova, false);
        var first = Service(db1);
        var second = Service(db2);
        // Ikkalasi ham arizani bir xil holatda o'qigan: ikkinchisining qarori versiya to'qnashuvi bilan rad etiladi
        await db1.AccessRequests.Include(r => r.Approvals).SingleAsync(r => r.Id == request.Id, Ct);
        await db2.AccessRequests.Include(r => r.Approvals).SingleAsync(r => r.Id == request.Id, Ct);
        var a = await first.DecideAsync(rahimova, request.Id, true, null, Ct);
        var b = await second.DecideAsync(rahimova, request.Id, false, "Rad etaman", Ct);
        Assert.Equal(200, a.Status);
        Assert.Equal(409, b.Status);
    }

    [Fact]
    public async Task Leaver_loses_requested_roles_and_pending_requests_are_cancelled()
    {
        var pending = await CreateAsync("a.karimov", _kredit);
        await using (var db = pg.CreateContext())
        {
            db.IdentityRoles.Add(new IdentityRole { IdentityId = _karimov, RoleId = _hidden, Source = RoleAssignmentSource.Manual });
            (await db.Identities.SingleAsync(i => i.Id == _karimov, Ct)).Status = IdentityStatus.Terminated;
            await db.SaveChangesAsync(Ct);
        }
        await using (var db = pg.CreateContext())
            await new BirthrightService(db, new AuditLog(db, TimeProvider.System), TimeProvider.System).RecalculateAsync("system", null, Ct);

        await using var check = pg.CreateContext();
        Assert.Equal(AccessRequestStatus.Cancelled, (await check.AccessRequests.SingleAsync(r => r.Id == pending.Id, Ct)).Status);
        Assert.False(await check.IdentityRoles.AnyAsync(r => r.IdentityId == _karimov && r.ValidTo == null, Ct));
    }
}
