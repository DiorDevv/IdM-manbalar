using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Hr;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Idm.Tests;

/// <summary>Har bir test o'z bazasida: AD va HR sinxronlash natijalari bir-biriga aralashmasin.</summary>
public class ConnectorApiTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private sealed class FakeFactory(FakeConnector connector, FakeHrSource hr) : IConnectorFactory
    {
        public IConnector Create(string code, ConnectorDefinition definition) => connector;
        public IHrSource CreateHrSource(string code, ConnectorDefinition definition) => hr;
    }

    private readonly FakeConnector _connector = new() { Snapshot = TestDirectory.Snapshot() };
    private readonly FakeHrSource _hr = new();
    private WebApplicationFactory<Program> _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Idm", pg.ConnectionString);
            b.UseSetting("Idm:Sso:Authority", "https://sso.invalid/master");
            b.UseSetting("Idm:BootstrapAdmins:0", "boss");
            b.UseSetting("Idm:Connectors:AD:Name", "Active Directory");
            b.UseSetting("Idm:Connectors:AD:IntervalMinutes", "0");   // faqat qo'lda: jadval testga aralashmasin
            b.UseSetting("Idm:Connectors:AD:PrivilegedGroups:0", "Domain Admins");
            b.UseSetting("Idm:Connectors:HR:Name", "Kadrlar tizimi");
            b.UseSetting("Idm:Connectors:HR:Type", "hr-csv");
            b.UseSetting("Idm:Connectors:HR:IntervalMinutes", "0");
            b.UseSetting("Idm:Connectors:HR:MaxDeletePercent", "50");   // 3 xodimdan 1 tasi ketadi
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IConnectorFactory>(new FakeFactory(_connector, _hr));
                s.AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
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

    private static async Task<ConnectorRunItem> WaitForRunAsync(HttpClient client, Guid runId, string code = "AD")
    {
        for (var i = 0; i < 100; i++)
        {
            var runs = await client.GetFromJsonAsync<PagedResult<ConnectorRunItem>>($"/api/applications/{code}/runs", Json, Ct);
            var run = runs!.Items.FirstOrDefault(r => r.Id == runId);
            if (run is not null && run.Status != ConnectorRunStatus.Running) return run;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("Sinxronlash tugamadi");
    }

    [Fact]
    public async Task Admin_runs_a_sync_and_the_portal_sees_accounts_groups_and_the_identity_card()
    {
        var admin = Client("boss");
        // Konnektor sozlangani uchun AD tizimi ishga tushishda o'zi ro'yxatdan o'tadi
        var apps = await admin.GetFromJsonAsync<List<ApplicationItem>>("/api/applications", Json, Ct);
        Assert.True(Assert.Single(apps!, a => a.Code == "AD").HasConnector);

        var started = await admin.PostAsync("/api/applications/AD/sync", null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var run = await WaitForRunAsync(admin, (await started.Content.ReadFromJsonAsync<SyncStarted>(Json, Ct))!.RunId);
        Assert.Equal(ConnectorRunStatus.Succeeded, run.Status);
        Assert.Equal("boss", run.TriggeredBy);

        var details = await admin.GetFromJsonAsync<ApplicationDetails>("/api/applications/AD", Json, Ct);
        Assert.Equal((4, 1, 2, 1), (details!.EnabledAccounts, details.OrphanAccounts, details.Entitlements, details.PrivilegedEntitlements));

        var orphans = await admin.GetFromJsonAsync<PagedResult<ApplicationAccountItem>>("/api/applications/AD/accounts?orphan=true", Json, Ct);
        Assert.Equal("test.user", Assert.Single(orphans!.Items).Login);
        var groups = await admin.GetFromJsonAsync<PagedResult<EntitlementItem>>("/api/applications/AD/entitlements", Json, Ct);
        Assert.Equal(2, groups!.Items.Single(g => g.Name == "kredit-bolimi").MemberCount);

        var identities = await admin.GetFromJsonAsync<PagedResult<IdentityListItem>>("/api/identities?search=TAB-1", Json, Ct);
        var karimovId = Assert.Single(identities!.Items).Id;
        var card = await admin.GetFromJsonAsync<IdentityDetails>($"/api/identities/{karimovId}", Json, Ct);
        Assert.Equal("Rahimova Dilnoza", card!.ManagerName);
        Assert.Equal("kredit-bolimi", Assert.Single(Assert.Single(card.Accounts).Entitlements).Name);

        var dashboard = await admin.GetFromJsonAsync<DashboardSummary>("/api/dashboard", Json, Ct);
        Assert.Equal(ConnectorRunStatus.Succeeded, Assert.Single(dashboard!.Connectors, c => c.Code == "AD").LastRun!.Status);

        // Ikkinchi ishga tushish: o'zgarish xodim tarixida ko'rinadi
        _connector.Snapshot = TestDirectory.Snapshot(TestDirectory.Karimov with { IsEnabled = false }, TestDirectory.Rahimova, TestDirectory.Aliyev, TestDirectory.Unknown);
        started = await admin.PostAsync("/api/applications/AD/sync", null, Ct);
        await WaitForRunAsync(admin, (await started.Content.ReadFromJsonAsync<SyncStarted>(Json, Ct))!.RunId);
        var history = await admin.GetFromJsonAsync<PagedResult<AuditItem>>($"/api/identities/{karimovId}/history", Json, Ct);
        Assert.Contains(history!.Items, e => e.Action == "account.disabled");
        Assert.Contains(history.Items, e => e.Action == "identity.updated");
    }

    private static async Task<ConnectorRunItem> SyncAsync(HttpClient admin, string code)
    {
        var started = await admin.PostAsync($"/api/applications/{code}/sync", null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        return await WaitForRunAsync(admin, (await started.Content.ReadFromJsonAsync<SyncStarted>(Json, Ct))!.RunId, code);
    }

    [Fact]
    public async Task Correlation_reports_show_leavers_with_active_accounts_orphans_and_jml_events()
    {
        var admin = Client("boss");
        // TAB-3 (j.aliyev) HR'da bor, AD'da faol; TAB-1 va TAB-2 HR'da; test.user AD'da tabel raqamisiz
        _hr.Snapshot = new HrSnapshot([new HrRecord("TAB-1", "Aziz", "Karimov"), new HrRecord("TAB-2", "Dilnoza", "Rahimova"), new HrRecord("TAB-3", "Jasur", "Aliyev")]);
        Assert.Equal(ConnectorRunStatus.Succeeded, (await SyncAsync(admin, "HR")).Status);
        Assert.Equal(ConnectorRunStatus.Succeeded, (await SyncAsync(admin, "AD")).Status);

        // Aliyev ishdan ketdi (HR), lekin AD akkaunti hali yoqilgan
        _hr.Snapshot = new HrSnapshot([new HrRecord("TAB-1", "Aziz", "Karimov"), new HrRecord("TAB-2", "Dilnoza", "Rahimova"),
            new HrRecord("TAB-3", "Jasur", "Aliyev", TerminationDate: new DateOnly(2020, 1, 1))]);
        var hrRun = await SyncAsync(admin, "HR");
        Assert.Equal((ConnectorRunStatus.Succeeded, 1), (hrRun.Status, hrRun.IdentitiesTerminated));

        var summary = await admin.GetFromJsonAsync<CorrelationSummary>("/api/reports/correlation", Json, Ct);
        Assert.Equal((1, 1, true), (summary!.LeaversWithActiveAccounts, summary.OrphanAccounts, summary.HrConnected));
        var leaver = Assert.Single((await admin.GetFromJsonAsync<PagedResult<LeaverItem>>("/api/reports/leavers-with-active-accounts", Json, Ct))!.Items);
        Assert.Equal(("TAB-3", "j.aliyev"), (leaver.EmployeeNumber, Assert.Single(leaver.ActiveAccounts).Login));
        var orphan = Assert.Single((await admin.GetFromJsonAsync<PagedResult<OrphanAccountItem>>("/api/reports/orphan-accounts", Json, Ct))!.Items);
        Assert.Equal("test.user", orphan.Login);
        var events = await admin.GetFromJsonAsync<PagedResult<IdentityEventItem>>("/api/events?kind=Leaver", Json, Ct);
        Assert.Equal("TAB-3", Assert.Single(events!.Items).EmployeeNumber);
        Assert.Equal(1, (await admin.GetFromJsonAsync<DashboardSummary>("/api/dashboard", Json, Ct))!.LeaversWithActiveAccounts);

        Assert.Equal(HttpStatusCode.Forbidden, (await Client("a.karimov").GetAsync("/api/reports/correlation", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("a.karimov").GetAsync("/api/events", Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_admins_can_start_a_sync_and_unknown_systems_are_404()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("a.karimov").PostAsync("/api/applications/AD/sync", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("a.karimov").GetAsync("/api/applications/AD/accounts", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client("boss").PostAsync("/api/applications/ABS/sync", null, Ct)).StatusCode);
    }
}
