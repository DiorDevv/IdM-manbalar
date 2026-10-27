using System.Diagnostics;
using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Hr;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Bank.Idm.Infrastructure.Provisioning;
using Bank.Idm.Infrastructure.Reports;
using Bank.Idm.Infrastructure.Reviews;
using Bank.Idm.Infrastructure.Requests;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Tests;

/// <summary>
/// Yuklama testi: bank miqyosi — 10 000+ xodim. Faqat IDM_LOAD_TEST bilan (CI'da emas, uzoq ishlaydi):
/// IDM_LOAD_TEST=10000 dotnet test --filter-class '*LoadTests'. Har bir amalning vaqti chiqariladi va chegara bilan tekshiriladi.
/// </summary>
public class LoadTests(ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly int Size = int.TryParse(Environment.GetEnvironmentVariable("IDM_LOAD_TEST"), out var n) ? n : 0;
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const int Groups = 300, Departments = 40, Titles = 25;
    private readonly List<(string Step, TimeSpan Time)> _timings = [];

    public async ValueTask InitializeAsync()
    {
        if (Size > 0) await pg.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Size > 0) await pg.DisposeAsync();
    }

    private async Task<T> Measure<T>(string step, TimeSpan limit, Func<IdmDbContext, Task<T>> action)
    {
        await using var db = pg.CreateContext();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var watch = Stopwatch.StartNew();
        var result = await action(db);
        watch.Stop();
        _timings.Add((step, watch.Elapsed));
        output.WriteLine($"{step,-48} {watch.Elapsed.TotalSeconds,8:F2} s");
        Assert.True(watch.Elapsed < limit, $"{step}: {watch.Elapsed.TotalSeconds:F1} s, chegara {limit.TotalSeconds} s");
        return result;
    }

    [Fact]
    public async Task Bank_scale_sync_roles_provisioning_reports_and_review_stay_within_limits()
    {
        Assert.SkipWhen(Size == 0, "IDM_LOAD_TEST ko'rsatilmagan");
        var random = new Random(42);
        var minute = TimeSpan.FromMinutes(1);

        // HR: har bo'limda boshliq, qolganlar unga bo'ysunadi; 2% ishdan ketgan
        string Department(int i) => $"Bo'lim {i % Departments}";
        var hr = new FakeHrSource
        {
            Snapshot = new HrSnapshot(Enumerable.Range(0, Size).Select(i => new HrRecord($"T{i:D6}", $"Ism{i}", $"Familiya{i}",
                Department: Department(i), JobTitle: $"Lavozim {i % Titles}", ManagerEmployeeNumber: i < Departments ? null : $"T{i % Departments:D6}",
                HireDate: new DateOnly(2020, 1, 1), Status: i % 50 == 7 ? HrStatus.Terminated : HrStatus.Active)).ToList())
        };
        // AD: har xodimga akkaunt, o'rtacha 6 ta guruh; 1% egasiz akkaunt
        var groups = Enumerable.Range(0, Groups).Select(g => new ExternalGroup($"g{g}", $"guruh-{g}", DistinguishedName: $"CN=guruh-{g}")).ToList();
        var accounts = Enumerable.Range(0, Size + Size / 100).Select(i => new ExternalAccount($"u{i}", $"login{i}", true,
            EmployeeNumber: i < Size ? $"T{i:D6}" : null, FirstName: $"Ism{i}", LastName: $"Familiya{i}", DistinguishedName: $"CN=login{i}",
            GroupIds: Enumerable.Range(0, 6).Select(_ => $"g{random.Next(Groups)}").Distinct().ToList())).ToList();
        var ad = new FakeConnector { Snapshot = new ConnectorSnapshot(accounts, groups) };

        Guid hrApp, adApp;
        await using (var db = pg.CreateContext())
        {
            var h = new Application { Code = "HR", Name = "HR", Kind = ApplicationKind.HumanResources };
            var a = new Application { Code = "AD", Name = "AD", Kind = ApplicationKind.Directory };
            db.AddRange(h, a);
            await db.SaveChangesAsync(Ct);
            (hrApp, adApp) = (h.Id, a.Id);
        }
        var time = TimeProvider.System;
        async Task<ConnectorRun> Hr(IdmDbContext db)
        {
            var sync = new HrSync(db, new AuditLog(db, time), time);
            var run = (await sync.StartRunAsync(hrApp, ConnectorRunTrigger.Schedule, "system", ct: Ct))!;
            return await sync.ExecuteAsync(run.Id, hr, new HrSettings(MaxDeletePercent: 100), null, ct: Ct);
        }
        async Task<ConnectorRun> Ad(IdmDbContext db)
        {
            var sync = new ConnectorSync(db, new AuditLog(db, time), time);
            var run = (await sync.StartRunAsync(adApp, ConnectorRunTrigger.Schedule, "system", ct: Ct))!;
            return await sync.ExecuteAsync(run.Id, ad, new SyncSettings(CreateIdentities: false), null, ct: Ct);
        }

        Assert.Equal(ConnectorRunStatus.Succeeded, (await Measure("HR birinchi yuklash", 2 * minute, Hr)).Status);
        Assert.Equal(ConnectorRunStatus.Succeeded, (await Measure("AD birinchi yuklash (akkaunt, guruh, a'zolik)", 3 * minute, Ad)).Status);
        var again = await Measure("AD qayta sinxronlash (o'zgarishsiz)", 2 * minute, Ad);
        Assert.Equal((0, 0, 0), (again.AccountsCreated, again.MembershipsAdded, again.MembershipsRemoved));

        // Rollar: har bo'lim uchun avtomatik rol (3 guruh), 20 ta SoD qoidasi
        await using (var db = pg.CreateContext())
        {
            var entitlements = await db.Entitlements.Where(e => e.ApplicationId == adApp).OrderBy(e => e.ExternalId).ToListAsync(Ct);
            for (var d = 0; d < Departments; d++)
            {
                var role = new Role { Code = $"R{d}", Name = $"Bo'lim {d} xodimi" };
                foreach (var e in entitlements.Skip(d * 3).Take(3)) role.Entitlements.Add(new RoleEntitlement { EntitlementId = e.Id });
                role.Rules.Add(new RoleRule { Department = $"Bo'lim {d}", CreatedBy = "load" });
                db.Roles.Add(role);
            }
            for (var s = 0; s < 20; s++)
            {
                var rule = new SodRule { Code = $"SOD{s}", Name = $"Qoida {s}", CreatedBy = "load" };
                rule.Entitlements.Add(new SodRuleEntitlement { EntitlementId = entitlements[200 + s].Id, Side = SodSide.Left });
                rule.Entitlements.Add(new SodRuleEntitlement { EntitlementId = entitlements[250 + s].Id, Side = SodSide.Right });
                db.SodRules.Add(rule);
            }
            await db.SaveChangesAsync(Ct);
        }
        var roles = await Measure("Avtomatik rollar (birinchi hisoblash)", 2 * minute,
            db => new BirthrightService(db, new AuditLog(db, time), time).RecalculateAsync("system", null, Ct));
        Assert.True(roles.Assigned > Size * 9 / 10);
        await Measure("Avtomatik rollar (o'zgarishsiz)", minute, db => new BirthrightService(db, new AuditLog(db, time), time).RecalculateAsync("system", null, Ct));

        var settings = new ProvisioningSettings(ProvisioningMode.Approval, MaxAutoOperations: int.MaxValue);
        var plan = await Measure("Tizimga yozish rejasi (dry-run)", 2 * minute,
            db => new ProvisioningPlanner(db, new AuditLog(db, time), time).PlanAsync(adApp, settings, "system", null, Ct));
        output.WriteLine($"   reja: {plan.Planned} amal");
        Assert.True(plan.Planned > 0);
        await Measure("Tizimga yozish rejasi (takror, o'zgarishsiz)", minute,
            db => new ProvisioningPlanner(db, new AuditLog(db, time), time).PlanAsync(adApp, settings, "system", null, Ct));

        foreach (var key in new[] { "access", "privileged", "sod", "orphans", "leavers" })
        {
            var table = await Measure($"Hisobot: {key}", minute, db => new ReportBuilder(db, new AccessAnalyzer(db, time), time).BuildAsync(key, null, Ct));
            output.WriteLine($"   {table!.Rows.Count} qator");
            if (key == "access")
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var bytes = await Measure("Excel: access", minute, _ => Task.FromResult(XlsxWriter.Write(table)));
                output.WriteLine($"   fayl {bytes.Length / 1024} KB, Excel yozishda {(GC.GetAllocatedBytesForCurrentThread() - before) / 1024 / 1024} MB ajratildi");
            }
        }
        await Measure("Ruxsatlar farqi (drift)", minute, db => new AccessAnalyzer(db, time).DriftAsync(Ct));

        var scope = new CampaignScope("Yuklama", null, null, null, false, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7));
        await Measure("Attestatsiya oldindan ko'rish (butun bank)", minute, db => new AccessReviewService(db, new AuditLog(db, time), time).PreviewAsync(scope, Ct));
        var campaign = await Measure("Attestatsiya kampaniyasi (butun bank)", 2 * minute, db => new AccessReviewService(db, new AuditLog(db, time), time)
            .CreateAsync(new RequestActor("boss", null, null, true), scope, Ct));
        output.WriteLine($"   {campaign.Value?.Items.Count} yozuv");

        var verified = await Measure("Audit zanjirini tekshirish", 2 * minute, db => new AuditLog(db, time).VerifyChainAsync(Ct));
        Assert.True(verified.IsValid);
        output.WriteLine($"   {verified.CheckedCount} audit yozuvi");
    }
}
