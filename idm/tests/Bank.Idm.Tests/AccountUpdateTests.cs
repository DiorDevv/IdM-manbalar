using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Ldap;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Provisioning;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Tests;

/// <summary>
/// Akkauntni HR holatiga keltirish (UpdateAccount): o'tishda bo'lim, lavozim, filial, rahbar va OU; ishdan ketganda — ishdan ketganlar OU'si;
/// qayta ishga olinganda — qaytarish. Karimov — Bosh ofisdan Chilonzorga o'tdi (Kassa, rahbari Aliyev); Aliyev — o'zgarishsiz;
/// Rahimova — ishdan ketgan; Yusupova — AD manbali (AD haqiqat manbai — yozilmaydi).
/// </summary>
public class AccountUpdateTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly MutableTime _time = new(new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero));
    private readonly FakeProvisioner _directory = new();
    private Guid _app, _karimov, _rahimova, _karimovAcc, _rahimovaAcc;
    private const string Base = "OU=Xodimlar,DC=bank,DC=test";
    private const string Bosh = "OU=Bosh ofis," + Base, Chilonzor = "OU=Chilonzor filiali," + Base, Leavers = "OU=Ishdan ketganlar," + Base;

    private static readonly ProvisioningSettings Settings = new(ProvisioningMode.Approval, UpdateAccounts: true,
        BranchContainers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Bosh ofis"] = Bosh, ["Chilonzor filiali"] = Chilonzor },
        DefaultContainer: Base, LeaversContainer: Leavers, ManageBranch: true);

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using var db = pg.CreateContext();
        var ad = new Application { Code = "AD", Name = "AD", Kind = ApplicationKind.Directory };
        var aliyev = new Identity { EmployeeNumber = "T4", FirstName = "Jasur", LastName = "Aliyev", Source = IdentitySource.Hr, Branch = "Bosh ofis", Department = "IT", JobTitle = "Boshliq" };
        var karimov = new Identity { EmployeeNumber = "T1", FirstName = "Aziz", LastName = "Karimov", Source = IdentitySource.Hr, Branch = "Chilonzor filiali",
            Department = "Kassa", JobTitle = "Kassir", Manager = aliyev };
        var rahimova = new Identity { EmployeeNumber = "T2", FirstName = "Dilnoza", LastName = "Rahimova", Source = IdentitySource.Hr, Status = IdentityStatus.Terminated,
            Branch = "Chilonzor filiali", Department = "Kredit" };
        var yusupova = new Identity { EmployeeNumber = "T3", FirstName = "Nodira", LastName = "Yusupova", Source = IdentitySource.ActiveDirectory, Department = "Buxgalteriya" };
        Account Acc(Identity i, string login, string ou, string? dept, string? title, string? branch, string? manager = null, AccountStatus status = AccountStatus.Enabled)
        {
            var a = new Account { Application = ad, Identity = i, ExternalId = $"u-{login}", Login = login, EmployeeNumber = i.EmployeeNumber, Status = status,
                DistinguishedName = $"CN={i.LastName} {i.FirstName},{ou}", Department = dept, JobTitle = title, Branch = branch, ManagerDn = manager,
                OrgSynced = true };
            _directory.Accounts[a.ExternalId] = new FakeProvisioner.Entry(a.ExternalId, login, i.EmployeeNumber, status == AccountStatus.Enabled);
            return a;
        }
        var karimovAcc = Acc(karimov, "a.karimov", Bosh, "Kredit", "Mutaxassis", "Bosh ofis");
        var rahimovaAcc = Acc(rahimova, "d.rahimova", Chilonzor, "Kredit", null, "Chilonzor filiali", status: AccountStatus.Disabled);
        db.AddRange(ad, aliyev, karimov, rahimova, yusupova, karimovAcc, rahimovaAcc,
            Acc(aliyev, "j.aliyev", Bosh, "IT", "Boshliq", "Bosh ofis"), Acc(yusupova, "n.yusupova", Bosh, "Boshqa bo'lim", null, null),
            new ConnectorRun { Application = ad, TriggeredBy = "system", Status = ConnectorRunStatus.Succeeded, StartedAt = _time.Now.AddMinutes(-5), FinishedAt = _time.Now.AddMinutes(-4) });
        await db.SaveChangesAsync(Ct);
        (_app, _karimov, _rahimova, _karimovAcc, _rahimovaAcc) = (ad.Id, karimov.Id, rahimova.Id, karimovAcc.Id, rahimovaAcc.Id);
    }

    public async ValueTask DisposeAsync() => await pg.DisposeAsync();

    private async Task<PlanResult> PlanAsync(ProvisioningSettings? settings = null)
    {
        await using var db = pg.CreateContext();
        return await new ProvisioningPlanner(db, new AuditLog(db, _time), _time).PlanAsync(_app, settings ?? Settings, "boss", "Boss", Ct);
    }

    private async Task ApproveAndExecuteAsync()
    {
        await using (var db = pg.CreateContext())
            await new ProvisioningService(db, new AuditLog(db, _time), _time).ApplyAsync(ProvisioningAction.Approve,
                await db.ProvisioningOperations.Where(o => o.Status == ProvisioningStatus.Proposed).Select(o => o.Id).ToListAsync(Ct),
                new HashSet<Guid> { _app }, "boss", "Boss", ct: Ct);
        await using (var db = pg.CreateContext())
            await new ProvisioningExecutor(db, new AuditLog(db, _time), _time).ExecuteDueAsync(_app, _directory, Settings, ct: Ct);
    }

    private async Task<List<ProvisioningOperation>> UpdatesAsync()
    {
        await using var db = pg.CreateContext();
        return await db.ProvisioningOperations.AsNoTracking().Where(o => o.Kind == ProvisioningKind.UpdateAccount).ToListAsync(Ct);
    }

    [Fact]
    public async Task Mover_and_leaver_accounts_are_planned_hr_is_the_source_and_nothing_happens_when_disabled()
    {
        await PlanAsync(Settings with { UpdateAccounts = false });
        Assert.Empty(await UpdatesAsync());

        await PlanAsync();
        var ops = await UpdatesAsync();
        Assert.Equal(2, ops.Count);   // Aliyev o'zgarishsiz, Yusupova — AD manbali
        var mover = ops.Single(o => o.AccountId == _karimovAcc);
        Assert.Equal("mover", mover.Reason);
        Assert.Contains("bo'lim: Kredit → Kassa", mover.Summary);
        Assert.Contains("lavozim: Mutaxassis → Kassir", mover.Summary);
        Assert.Contains("filial: Bosh ofis → Chilonzor filiali", mover.Summary);
        Assert.Contains("rahbar: — → CN=Aliyev Jasur", mover.Summary);
        Assert.Contains($"OU: {Bosh} → {Chilonzor}", mover.Summary);
        var leaver = ops.Single(o => o.AccountId == _rahimovaAcc);
        Assert.Equal("leaver", leaver.Reason);
        Assert.DoesNotContain("bo'lim", leaver.Summary);   // ishdan ketganda faqat joyi
        Assert.Contains($"→ {Leavers}", leaver.Summary);
    }

    [Fact]
    public async Task Executed_updates_reach_the_directory_and_are_not_planned_again()
    {
        await PlanAsync();
        await ApproveAndExecuteAsync();

        var mover = _directory.Updates["u-a.karimov"];
        Assert.Equal(new AccountAttributes("Kassa", "Kassir", "Chilonzor filiali", $"CN=Aliyev Jasur,{Bosh}"), mover.Attributes);
        Assert.Equal(Chilonzor, mover.Container);
        var leaver = _directory.Updates["u-d.rahimova"];
        Assert.Equal((null, Leavers), (leaver.Attributes, leaver.Container));

        await using (var db = pg.CreateContext())
        {
            var acc = await db.Accounts.SingleAsync(a => a.Id == _karimovAcc, Ct);
            Assert.Equal(($"CN=Karimov Aziz,{Chilonzor}", "Kassa", "Chilonzor filiali"), (acc.DistinguishedName, acc.Department, acc.Branch));
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "provisioning.account-updated", Ct));
        }
        var calls = _directory.Calls.Count;
        await PlanAsync();
        Assert.All(await UpdatesAsync(), o => Assert.NotEqual(ProvisioningStatus.Proposed, o.Status));
        Assert.Equal(calls, _directory.Calls.Count);
    }

    [Fact]
    public async Task A_rehired_employee_leaves_the_leavers_ou_unmapped_active_accounts_stay_where_they_are()
    {
        await using (var db = pg.CreateContext())
        {
            await db.Accounts.Where(a => a.Id == _rahimovaAcc).ExecuteUpdateAsync(s => s.SetProperty(a => a.DistinguishedName, $"CN=Rahimova Dilnoza,{Leavers}"), Ct);
            // Qayta ishga olindi, filiali xaritada yo'q
            await db.Identities.Where(i => i.Id == _rahimova).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, IdentityStatus.Active).SetProperty(i => i.Branch, "Samarqand"), Ct);
            await db.Identities.Where(i => i.Id == _karimov).ExecuteUpdateAsync(s => s.SetProperty(i => i.Branch, "Samarqand"), Ct);
        }
        await PlanAsync();
        var ops = await UpdatesAsync();
        Assert.Contains($"OU: {Leavers} → {Base}", ops.Single(o => o.AccountId == _rahimovaAcc).Summary);
        Assert.DoesNotContain("OU:", ops.Single(o => o.AccountId == _karimovAcc).Summary);   // xaritada yo'q filial — joyidan qo'zg'atilmaydi
    }

    [Fact]
    public async Task Accounts_whose_directory_state_was_never_read_are_not_compared()
    {
        // Yangilangan versiyadan keyin hali sinxronlanmagan: bo'lim va h.k. bo'sh — "— → X" taklifi bo'lmasligi kerak (stendda topilgan)
        await using (var db = pg.CreateContext())
            await db.Accounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.OrgSynced, false), Ct);
        await PlanAsync();
        Assert.Empty(await UpdatesAsync());
    }

    [Theory]
    [InlineData("CN=Karimov Aziz,OU=Bosh ofis,DC=bank,DC=test", "CN=Karimov Aziz", "OU=Bosh ofis,DC=bank,DC=test")]
    [InlineData(@"CN=Karimov\, Aziz,OU=X,DC=bank", @"CN=Karimov\, Aziz", "OU=X,DC=bank")]
    public void Dn_is_split_on_the_first_unescaped_comma(string dn, string rdn, string parent) =>
        Assert.Equal((rdn, parent), LdapProvisioner.SplitDn(dn));
}
