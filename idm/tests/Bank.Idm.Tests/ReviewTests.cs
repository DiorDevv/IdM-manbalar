using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Bank.Idm.Infrastructure.Provisioning;
using Bank.Idm.Infrastructure.Reports;
using Bank.Idm.Infrastructure.Requests;
using Bank.Idm.Infrastructure.Reviews;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Idm.Tests;

/// <summary>
/// Attestatsiya va hisobotlar. Bank holati: Rahimova — kredit bo'limi boshlig'i (rahbari yo'q), Karimov — uning xodimi: ariza bilan
/// "KREDIT" roli (kredit guruhi) va hech qaysi rol bilan asoslanmagan ABS guruhi; Rahimovada asoslanmagan Domain Admins (imtiyozli);
/// Aliyev — Karimovning xodimi, faqat avtomatik rol; Yusupova — ishdan ketgan; boss — IdM administratori (xodim yozuvi yo'q).
/// </summary>
public class ReviewTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly MutableTime _time = new(new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero));
    private DateOnly Today => DateOnly.FromDateTime(_time.Now.UtcDateTime);
    private WebApplicationFactory<Program>? _factory;
    private Guid _app, _karimov, _rahimova, _karimovAcc, _abs, _credit, _admins;

    private static readonly RequestActor Boss = new("boss", "Boss", null, IsAdmin: true);
    private RequestActor Rahimova => new("d.rahimova", "Rahimova", _rahimova, IsAdmin: false);
    private RequestActor Karimov => new("a.karimov", "Karimov", _karimov, IsAdmin: false);

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using var db = pg.CreateContext();
        var ad = new Application { Code = "AD", Name = "Active Directory", Kind = ApplicationKind.Directory };
        Entitlement Group(string id, string name, bool privileged = false) => new() { Application = ad, ExternalId = id, Name = name, IsPrivileged = privileged };
        var credit = Group("g-credit", "kredit-bolimi");
        var abs = Group("g-abs", "abs-operatorlar");
        var admins = Group("g-admins", "Domain Admins", privileged: true);
        var it = Group("g-it", "it-xodimlar");
        Identity Person(string number, string first, string last, Identity? manager = null, IdentityStatus status = IdentityStatus.Active) =>
            new() { EmployeeNumber = number, FirstName = first, LastName = last, Manager = manager, Status = status, Department = "Kredit bo'limi", Source = IdentitySource.Hr };
        var rahimova = Person("T2", "Dilnoza", "Rahimova");
        var karimov = Person("T1", "Aziz", "Karimov", rahimova);
        var aliyev = Person("T3", "Jasur", "Aliyev", karimov);
        aliyev.Department = "IT";
        var yusupova = Person("T4", "Nodira", "Yusupova", rahimova, IdentityStatus.Terminated);
        Account Acc(Identity i, string login, params Entitlement[] groups)
        {
            var a = new Account { Application = ad, Identity = i, ExternalId = $"u-{login}", Login = login, EmployeeNumber = i.EmployeeNumber };
            foreach (var g in groups) a.Entitlements.Add(new AccountEntitlement { Entitlement = g });
            return a;
        }
        var karimovAcc = Acc(karimov, "a.karimov", credit, abs);
        var kredit = new Role { Code = "KREDIT", Name = "Kredit mutaxassisi", Risk = RiskLevel.Medium };
        kredit.Entitlements.Add(new RoleEntitlement { Entitlement = credit });
        var itRole = new Role { Code = "IT", Name = "IT xodimi" };
        itRole.Entitlements.Add(new RoleEntitlement { Entitlement = it });
        db.AddRange(ad, credit, abs, admins, it, rahimova, karimov, aliyev, yusupova, karimovAcc, Acc(rahimova, "d.rahimova", admins), Acc(aliyev, "j.aliyev", it),
            Acc(yusupova, "n.yusupova", abs), kredit, itRole,
            new Account { Application = ad, ExternalId = "u-orphan", Login = "test.user", DisplayName = "=HYPERLINK(\"http://x\")" },
            new IdentityRole { Identity = karimov, Role = kredit, Source = RoleAssignmentSource.Request, ValidFrom = _time.Now.AddDays(-10) },
            new IdentityRole { Identity = aliyev, Role = itRole, Source = RoleAssignmentSource.Birthright, ValidFrom = _time.Now.AddDays(-10) },
            new ConnectorRun { Application = ad, TriggeredBy = "system", Status = ConnectorRunStatus.Succeeded, StartedAt = _time.Now.AddMinutes(-5) });
        await db.SaveChangesAsync(Ct);
        (_app, _karimov, _rahimova, _karimovAcc, _abs, _credit, _admins) = (ad.Id, karimov.Id, rahimova.Id, karimovAcc.Id, abs.Id, credit.Id, admins.Id);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await pg.DisposeAsync();
    }

    private AccessReviewService Service(IdmDbContext db) => new(db, new AuditLog(db, _time), _time);

    private CampaignScope Scope(string? department = null, bool privilegedOnly = false, Guid? app = null) =>
        new("2026 Q4 attestatsiya", null, department, app, privilegedOnly, Today.AddDays(14));

    private async Task<AccessReviewCampaign> CreateAsync(CampaignScope? scope = null)
    {
        await using var db = pg.CreateContext();
        var result = await Service(db).CreateAsync(Boss, scope ?? Scope(), Ct);
        return result.Value ?? throw new InvalidOperationException(result.Error);
    }

    private async Task<List<AccessReviewItem>> ItemsAsync()
    {
        await using var db = pg.CreateContext();
        return (await db.AccessReviewItems.AsNoTracking().ToListAsync(Ct)).OrderBy(i => i.Kind).ThenBy(i => i.Title).ToList();   // enum bazada matn
    }

    private async Task<ReviewResult<int>> DecideAsync(RequestActor actor, IEnumerable<Guid> ids, bool keep, string? comment = null)
    {
        await using var db = pg.CreateContext();
        return await Service(db).DecideAsync(actor, ids.ToList(), keep, comment, Ct);
    }

    [Fact]
    public async Task Campaign_covers_requested_roles_and_unjustified_groups_of_working_people_with_the_manager_as_reviewer()
    {
        await using (var db = pg.CreateContext())
        {
            var preview = (await Service(db).PreviewAsync(Scope(), Ct)).Value!;
            Assert.Equal(new CampaignPreview(Items: 3, Roles: 1, Entitlements: 2, Privileged: 1, Identities: 2, Reviewers: 1, AdminPool: 1), preview);
            Assert.Equal(1, (await Service(db).PreviewAsync(Scope(privilegedOnly: true), Ct)).Value!.Items);
            Assert.Equal(0, (await Service(db).PreviewAsync(Scope(department: "IT"), Ct)).Value!.Items);   // Aliyevda faqat avtomatik rol
            Assert.Equal(400, (await Service(db).CreateAsync(Boss, Scope(department: "IT"), Ct)).Status);
            Assert.Equal(400, (await Service(db).PreviewAsync(Scope() with { DueDate = Today.AddDays(-1) }, Ct)).Status);
            Assert.Equal(400, (await Service(db).PreviewAsync(Scope() with { Name = "Q" }, Ct)).Status);
        }

        var campaign = await CreateAsync();

        var items = await ItemsAsync();
        Assert.Equal(new[]
        {
            (AccessReviewItemKind.Role, "Kredit mutaxassisi", "KREDIT", _karimov, (Guid?)_rahimova, false),
            (AccessReviewItemKind.Entitlement, "abs-operatorlar", "a.karimov", _karimov, _rahimova, false),
            (AccessReviewItemKind.Entitlement, "Domain Admins", "d.rahimova", _rahimova, null, true),
        }, items.Select(i => (i.Kind, i.Title, i.Subtitle!, i.IdentityId, i.ReviewerId, i.IsPrivileged)));
        Assert.All(items, i => Assert.Equal(campaign.Id, i.CampaignId));
        await using var check = pg.CreateContext();
        Assert.True(await check.AuditEvents.AnyAsync(e => e.Action == "review.campaign-created" && e.TargetId == campaign.Id.ToString(), Ct));
    }

    [Fact]
    public async Task Only_the_assigned_reviewer_decides_nobody_reviews_themselves_and_decisions_are_final()
    {
        await CreateAsync();
        var items = await ItemsAsync();
        var karimovItems = items.Where(i => i.IdentityId == _karimov).Select(i => i.Id).ToList();
        var adminItem = items.Single(i => i.ReviewerId is null).Id;

        Assert.Equal(403, (await DecideAsync(Karimov, karimovItems, keep: true)).Status);   // o'z ruxsati
        Assert.Equal(403, (await DecideAsync(Boss, karimovItems, keep: true)).Status);      // rahbarga tegishli
        Assert.Equal(403, (await DecideAsync(Rahimova, [adminItem], keep: true)).Status);   // o'z ruxsati (administrator navbatida)
        Assert.Equal(403, (await DecideAsync(Rahimova, [.. karimovItems, adminItem], keep: true)).Status);   // hammasi yoki hech biri
        Assert.All(await ItemsAsync(), i => Assert.Equal(AccessReviewDecision.Pending, i.Decision));

        Assert.Equal(2, (await DecideAsync(Rahimova, karimovItems, keep: true, "Ishi uchun kerak")).Value);
        Assert.Equal(1, (await DecideAsync(Boss, [adminItem], keep: true)).Value);
        Assert.Equal(409, (await DecideAsync(Rahimova, karimovItems, keep: false)).Status);
        Assert.All(await ItemsAsync(), i => Assert.Equal(AccessReviewDecision.Keep, i.Decision));
        await using var db = pg.CreateContext();
        Assert.Equal(3, await db.AuditEvents.CountAsync(e => e.Action == "review.kept", Ct));
    }

    [Fact]
    public async Task Revoked_role_ends_at_once_and_revoked_group_is_removed_from_the_directory_through_provisioning()
    {
        await CreateAsync();
        var items = await ItemsAsync();
        var role = items.Single(i => i.Kind == AccessReviewItemKind.Role);
        var abs = items.Single(i => i.Title == "abs-operatorlar");

        Assert.Equal(2, (await DecideAsync(Rahimova, [role.Id, abs.Id], keep: false, "Boshqa bo'limga o'tgan")).Value);

        await using (var db = pg.CreateContext())
        {
            Assert.NotNull((await db.IdentityRoles.SingleAsync(r => r.IdentityId == _karimov, Ct)).ValidTo);
            Assert.NotNull((await db.AccessReviewItems.SingleAsync(i => i.Id == role.Id, Ct)).RemediatedAt);
            Assert.Null((await db.AccessReviewItems.SingleAsync(i => i.Id == abs.Id, Ct)).RemediatedAt);
            Assert.Contains("\"source\":\"review\"", (await db.AuditEvents.SingleAsync(e => e.Action == "identity.role-removed", Ct)).Details);
        }

        // Rejalashtiruvchi: ortiqcha ABS guruhi olinadi; kredit guruhi IdM qo'shmagani uchun qoladi (qo'lda berilgan — keyingi attestatsiyada)
        var directory = new FakeProvisioner();
        var settings = new ProvisioningSettings(ProvisioningMode.Automatic, ProtectedLogins: ["n.yusupova"]);   // ishdan ketgan Yusupova — boshqa test mavzusi
        await using (var db = pg.CreateContext())
            await new ProvisioningPlanner(db, new AuditLog(db, _time), _time).PlanAsync(_app, settings, "system", null, Ct);
        await using (var db = pg.CreateContext())
        {
            var op = await db.ProvisioningOperations.SingleAsync(Ct);
            Assert.Equal((ProvisioningKind.RemoveMembership, _karimovAcc, _abs, "review-revoked"), (op.Kind, op.AccountId!.Value, op.EntitlementId!.Value, op.Reason));
            Assert.Equal(0, await Service(db).RemediateAsync("system", null, Ct));   // hali tizimda
            await new ProvisioningExecutor(db, new AuditLog(db, _time), _time).ExecuteDueAsync(_app, directory, settings, ct: Ct);
        }
        Assert.Equal(["remove u-a.karimov g-abs"], directory.Calls);

        await using (var db = pg.CreateContext())
        {
            Assert.Equal(1, await Service(db).RemediateAsync("system", null, Ct));
            Assert.NotNull((await db.AccessReviewItems.SingleAsync(i => i.Id == abs.Id, Ct)).RemediatedAt);
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "review.remediated", Ct));
            // Bajarilgandan keyin reja bo'sh
            Assert.Equal(0, (await new ProvisioningPlanner(db, new AuditLog(db, _time), _time).PlanAsync(_app, settings, "system", null, Ct)).Planned);
        }
    }

    [Fact]
    public async Task Closed_campaign_accepts_no_decisions_and_keeps_its_result()
    {
        var campaign = await CreateAsync();
        var first = (await ItemsAsync()).First(i => i.ReviewerId == _rahimova);
        await DecideAsync(Rahimova, [first.Id], keep: true);
        await using (var db = pg.CreateContext())
        {
            Assert.Equal(AccessReviewStatus.Completed, (await Service(db).CloseAsync(Boss, campaign.Id, cancel: false, Ct)).Value!.Status);
            Assert.Equal(409, (await Service(db).CloseAsync(Boss, campaign.Id, cancel: true, Ct)).Status);
        }

        Assert.Equal(409, (await DecideAsync(Boss, (await ItemsAsync()).Where(i => i.ReviewerId is null).Select(i => i.Id), keep: true)).Status);

        await using var check = pg.CreateContext();
        var report = (await new ReportBuilder(check, new AccessAnalyzer(check, _time), _time).BuildAsync("campaign", campaign.Id, Ct))!;
        Assert.Contains("ko'rib chiqildi 1 / 3", report.Subtitle);
        Assert.Equal(1, report.Rows.Count(r => r[9] == "Qoldirildi"));
        Assert.Equal(2, report.Rows.Count(r => r[9] == "Ko'rib chiqilmagan"));
        Assert.Contains("\"Pending\":2", (await check.AuditEvents.SingleAsync(e => e.Action == "review.campaign-completed", Ct)).Details);
    }

    [Fact]
    public async Task Reports_show_who_has_what_privileged_accounts_and_orphans()
    {
        await using var db = pg.CreateContext();
        var builder = new ReportBuilder(db, new AccessAnalyzer(db, _time), _time);

        var access = (await builder.BuildAsync("access", null, Ct))!;
        string? Basis(string login, string group) => access.Rows.Single(r => r[6] == login && r[8] == group)[10];
        Assert.Equal("KREDIT", Basis("a.karimov", "kredit-bolimi"));
        Assert.Equal("asossiz (ortiqcha)", Basis("a.karimov", "abs-operatorlar"));
        Assert.Equal("Ishdan ketgan", access.Rows.Single(r => r[6] == "n.yusupova")[4]);

        var privileged = (await builder.BuildAsync("privileged", null, Ct))!;
        var row = Assert.Single(privileged.Rows);
        Assert.Equal(("d.rahimova", "Domain Admins", null), (row[1], row[7], row[8]));

        var orphan = Assert.Single((await builder.BuildAsync("orphans", null, Ct))!.Rows);
        Assert.Equal("test.user", orphan[1]);
        Assert.Equal("n.yusupova", Assert.Single((await builder.BuildAsync("leavers", null, Ct))!.Rows)[5]);
        Assert.Null(await builder.BuildAsync("campaign", Guid.NewGuid(), Ct));
        Assert.Null(await builder.BuildAsync("unknown", null, Ct));
    }

    [Fact]
    public async Task Excel_file_is_a_valid_workbook_and_formulas_are_kept_as_text()
    {
        await using var db = pg.CreateContext();
        var table = (await new ReportBuilder(db, new AccessAnalyzer(db, _time), _time).BuildAsync("orphans", null, Ct))! with { Title = "Egasiz: akkauntlar/[test]" };

        using var zip = new ZipArchive(new MemoryStream(XlsxWriter.Write(table)));
        Assert.Equal(["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/workbook.xml", "xl/styles.xml", "xl/worksheets/sheet1.xml"],
            zip.Entries.Select(e => e.FullName));
        XDocument Load(string name) { using var s = zip.GetEntry(name)!.Open(); return XDocument.Load(s); }
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Equal("Egasiz akkauntlartest", Load("xl/workbook.xml").Descendants(ns + "sheet").Single().Attribute("name")!.Value);
        var sheet = Load("xl/worksheets/sheet1.xml");
        var rows = sheet.Descendants(ns + "row").ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows[0].Elements(ns + "c"), c => Assert.Equal("1", c.Attribute("s")!.Value));
        Assert.Empty(sheet.Descendants(ns + "f"));
        Assert.Contains(rows[1].Descendants(ns + "t"), t => t.Value == "=HYPERLINK(\"http://x\")");
        Assert.Equal("A1:G2", sheet.Descendants(ns + "autoFilter").Single().Attribute("ref")!.Value);
    }

    [Fact]
    public void Excel_helpers_name_columns_and_drop_characters_xml_cannot_hold()
    {
        Assert.Equal(["A", "Z", "AA", "AZ", "BA"], new[] { 0, 25, 26, 51, 52 }.Select(XlsxWriter.Column));
        Assert.Equal("a&lt;b&gt;&amp;\"c😀", XlsxWriter.Escape("a<b>&\u0001\"c😀\ud800").Replace("&quot;", "\""));
    }

    private HttpClient Client(string user)
    {
        _factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Idm", pg.ConnectionString);
            b.UseSetting("Idm:Sso:Authority", "https://sso.invalid/master");
            b.UseSetting("Idm:ProvisioningIntervalSeconds", "0");
            b.UseSetting("Idm:BootstrapAdmins:0", "boss");
            b.ConfigureTestServices(s => s.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user);
        return client;
    }

    [Fact]
    public async Task Portal_flow_admin_launches_manager_decides_auditor_exports()
    {
        var boss = Client("boss");
        var body = new { name = "Q4 attestatsiya", dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7), privilegedOnly = false };
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("d.rahimova").PostAsJsonAsync("/api/reviews/campaigns", body, Ct)).StatusCode);
        var preview = await (await boss.PostAsJsonAsync("/api/reviews/campaigns/preview", body, Ct)).Content.ReadFromJsonAsync<CampaignPreview>(Json, Ct);
        Assert.Equal(3, preview!.Items);
        var created = await boss.PostAsJsonAsync("/api/reviews/campaigns", body, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var campaign = (await created.Content.ReadFromJsonAsync<CampaignItem>(Json, Ct))!;
        Assert.Equal((3, 3), (campaign.Total, campaign.Pending));

        // Rahbar: o'z xodimining 2 yozuvi; xodim: hech narsa va kampaniyalarni ko'ra olmaydi
        var rahimova = Client("d.rahimova");
        Assert.Equal(2, (await rahimova.GetFromJsonAsync<MeResponse>("/api/me", Json, Ct))!.ReviewCount);
        var inbox = (await rahimova.GetFromJsonAsync<List<ReviewItemDto>>("/api/reviews/inbox", Json, Ct))!;
        Assert.All(inbox, i => Assert.Equal("T1", i.EmployeeNumber));
        var karimov = Client("a.karimov");
        Assert.Empty((await karimov.GetFromJsonAsync<List<ReviewItemDto>>("/api/reviews/inbox", Json, Ct))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await karimov.GetAsync("/api/reviews/campaigns", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await karimov.PostAsJsonAsync("/api/reviews/decide", new { ids = inbox.Select(i => i.Id), keep = true }, Ct)).StatusCode);

        var decided = await rahimova.PostAsJsonAsync("/api/reviews/decide", new { ids = inbox.Select(i => i.Id), keep = false, comment = "Kerak emas" }, Ct);
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal(0, (await rahimova.GetFromJsonAsync<MeResponse>("/api/me", Json, Ct))!.ReviewCount);
        Assert.Equal(1, (await boss.GetFromJsonAsync<MeResponse>("/api/me", Json, Ct))!.ReviewCount);   // administratorlar navbati

        var details = (await boss.GetFromJsonAsync<CampaignDetails>($"/api/reviews/campaigns/{campaign.Id}", Json, Ct))!;
        Assert.Equal((2, 1, 1), (details.Campaign.Revoked, details.Campaign.Pending, details.Campaign.RevokedOpen));
        Assert.Contains(details.Reviewers, r => r.ReviewerName == "Rahimova Dilnoza" && r.Pending == 0);

        // Eksport: Excel fayli va chop etish uchun JSON, ikkalasi auditda
        var xlsx = await boss.GetAsync($"/api/reports/export/campaign?format=xlsx&campaignId={campaign.Id}", Ct);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("idm-campaign-", xlsx.Content.Headers.ContentDisposition!.FileNameStar ?? xlsx.Content.Headers.ContentDisposition.FileName);
        var table = (await boss.GetFromJsonAsync<ReportTable>("/api/reports/export/access", Json, Ct))!;
        Assert.Equal("Kirish huquqlari", table.Title);
        Assert.Equal(HttpStatusCode.NotFound, (await boss.GetAsync("/api/reports/export/salary", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await karimov.GetAsync("/api/reports/export/access", Ct)).StatusCode);
        await using var db = pg.CreateContext();
        Assert.Equal(2, await db.AuditEvents.CountAsync(e => e.Action == "report.exported" && e.ActorId == "boss", Ct));
    }
}
