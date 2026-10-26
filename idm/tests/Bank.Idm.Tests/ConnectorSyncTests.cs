using Bank.Idm.Connectors;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Tests;

/// <summary>Tarmoqsiz konnektor: test o'zi tizim holatini beradi yoki xato qaytaradi.</summary>
public class FakeConnector : IConnector
{
    public ConnectorSnapshot Snapshot { get; set; } = new([], []);
    public Exception? Error { get; set; }
    public Task<ConnectorSnapshot> ReadAsync(CancellationToken ct = default) => Error is null ? Task.FromResult(Snapshot) : Task.FromException<ConnectorSnapshot>(Error);
}

/// <summary>Bank AD'sining kichik nusxasi: 4 xodim (biri tabel raqamisiz), 2 guruh.</summary>
public static class TestDirectory
{
    public const string Credit = "g-credit", Admins = "g-admins";
    public static ExternalAccount Karimov => new("u-1", "a.karimov", true, "TAB-1", "Aziz", "Karimov", Email: "a.karimov@bank.test",
        Department: "Kredit bo'limi", JobTitle: "Kredit mutaxassisi", ManagerDn: "CN=d.rahimova", DistinguishedName: "CN=a.karimov", GroupIds: [Credit]);
    public static ExternalAccount Rahimova => new("u-2", "d.rahimova", true, "TAB-2", "Dilnoza", "Rahimova", Department: "Kredit bo'limi",
        JobTitle: "Bo'lim boshlig'i", DistinguishedName: "CN=d.rahimova", GroupIds: [Credit]);
    public static ExternalAccount Aliyev => new("u-3", "j.aliyev", true, "TAB-3", "Jasur", "Aliyev", Department: "IT", DistinguishedName: "CN=j.aliyev", GroupIds: [Admins]);
    public static ExternalAccount Unknown => new("u-4", "test.user", true, DistinguishedName: "CN=test.user");
    public static ExternalGroup[] Groups => [new(Credit, "kredit-bolimi"), new(Admins, "Domain Admins")];
    public static ConnectorSnapshot Snapshot(params ExternalAccount[] accounts) => new(accounts.Length == 0 ? [Karimov, Rahimova, Aliyev, Unknown] : accounts, Groups);
}

public class ConnectorSyncTests(PostgresFixture pg) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly SyncSettings Settings = new(MaxDeletePercent: 30, PrivilegedGroups: ["Domain Admins"]);
    private readonly FakeConnector _connector = new() { Snapshot = TestDirectory.Snapshot() };
    private Guid _appId;

    public async ValueTask InitializeAsync()
    {
        // Har bir test o'z tizimi bilan: testlar bitta bazada bir-biriga ta'sir qilmaydi
        await using var db = pg.CreateContext();
        var app = new Application { Code = $"AD{Guid.NewGuid():N}"[..12], Name = "Active Directory", Kind = ApplicationKind.Directory };
        db.Applications.Add(app);
        await db.SaveChangesAsync(Ct);
        _appId = app.Id;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<ConnectorRun> SyncAsync(bool allowMassDelete = false)
    {
        await using var db = pg.CreateContext();
        var sync = new ConnectorSync(db, new AuditLog(db, TimeProvider.System), TimeProvider.System);
        var run = await sync.StartRunAsync(_appId, ConnectorRunTrigger.Manual, "boss", Ct);
        return await sync.ExecuteAsync(run.Id, _connector, Settings, "Boss", allowMassDelete, Ct);
    }

    private async Task<List<Account>> AccountsAsync(IdmDbContext db) =>
        await db.Accounts.Where(a => a.ApplicationId == _appId).Include(a => a.Identity).Include(a => a.Entitlements).ThenInclude(e => e.Entitlement).ToListAsync(Ct);

    private async Task<List<AuditEvent>> RunEventsAsync(IdmDbContext db, ConnectorRun run) =>
        await db.AuditEvents.Where(e => e.OccurredAt >= run.StartedAt).OrderBy(e => e.Id).ToListAsync(Ct);

    // Tabel raqamlari har test uchun noyob bo'lishi kerak (identities.employee_number unique)
    private readonly string Prefix = Guid.NewGuid().ToString("N")[..8];
    private ExternalAccount Numbered(ExternalAccount a) => a with { EmployeeNumber = a.EmployeeNumber is null ? null : $"{Prefix}-{a.EmployeeNumber}" };
    private void SetDirectory(params ExternalAccount[] accounts) =>
        _connector.Snapshot = TestDirectory.Snapshot((accounts.Length == 0 ? [TestDirectory.Karimov, TestDirectory.Rahimova, TestDirectory.Aliyev, TestDirectory.Unknown] : accounts).Select(Numbered).ToArray());

    [Fact]
    public async Task First_load_creates_accounts_groups_memberships_and_ad_sourced_identities_with_a_single_audit_event()
    {
        SetDirectory();
        var run = await SyncAsync();

        Assert.Equal(ConnectorRunStatus.Succeeded, run.Status);
        Assert.Equal((4, 2, 4, 3, 3, 1), (run.AccountsRead, run.GroupsRead, run.AccountsCreated, run.IdentitiesCreated, run.MembershipsAdded, run.OrphanAccounts));

        await using var db = pg.CreateContext();
        var accounts = await AccountsAsync(db);
        var karimov = accounts.Single(a => a.Login == "a.karimov");
        Assert.Equal(IdentitySource.ActiveDirectory, karimov.Identity!.Source);
        Assert.Equal("Kredit mutaxassisi", karimov.Identity.JobTitle);
        Assert.Equal("kredit-bolimi", Assert.Single(karimov.Entitlements).Entitlement.Name);
        // Rahbar AD'dagi "manager" DN orqali bog'lanadi
        Assert.Equal(accounts.Single(a => a.Login == "d.rahimova").IdentityId, karimov.Identity.ManagerId);
        Assert.Null(accounts.Single(a => a.Login == "test.user").IdentityId);
        var admins = await db.Entitlements.SingleAsync(e => e.ApplicationId == _appId && e.Name == "Domain Admins", Ct);
        Assert.True(admins.IsPrivileged);

        var evt = Assert.Single(await RunEventsAsync(db, run));
        Assert.Equal("connector.initial-load", evt.Action);
        Assert.Equal("boss", evt.ActorId);
    }

    [Fact]
    public async Task Changes_after_the_first_load_are_applied_and_audited_one_by_one()
    {
        SetDirectory();
        await SyncAsync();
        SetDirectory(TestDirectory.Karimov with { IsEnabled = false, GroupIds = [TestDirectory.Admins], JobTitle = "Katta mutaxassis" },
            TestDirectory.Rahimova, TestDirectory.Aliyev, TestDirectory.Unknown);

        var run = await SyncAsync();

        Assert.Equal((1, 1, 1), (run.AccountsDisabled, run.MembershipsAdded, run.MembershipsRemoved));
        await using var db = pg.CreateContext();
        var karimov = (await AccountsAsync(db)).Single(a => a.Login == "a.karimov");
        Assert.Equal(AccountStatus.Disabled, karimov.Status);
        Assert.Equal("Domain Admins", Assert.Single(karimov.Entitlements).Entitlement.Name);
        Assert.Equal(IdentityStatus.Suspended, karimov.Identity!.Status);
        Assert.Equal("Katta mutaxassis", karimov.Identity.JobTitle);

        var actions = (await RunEventsAsync(db, run)).Select(e => e.Action).ToList();
        Assert.Equal(["account.disabled", "account.entitlement-removed", "account.entitlement-added", "identity.updated", "connector.sync-completed"], actions);
        Assert.True((await new AuditLog(db, TimeProvider.System).VerifyChainAsync(Ct)).IsValid);
    }

    [Fact]
    public async Task Mass_disappearance_is_aborted_until_an_administrator_approves_it()
    {
        SetDirectory();
        await SyncAsync();
        SetDirectory(TestDirectory.Karimov, TestDirectory.Unknown);   // 4 tadan 2 tasi (50%) yo'qoldi, chegara 30%

        var aborted = await SyncAsync();
        Assert.Equal(ConnectorRunStatus.Aborted, aborted.Status);
        Assert.Contains("50%", aborted.Error);
        await using (var db = pg.CreateContext())
        {
            Assert.All(await AccountsAsync(db), a => Assert.NotEqual(AccountStatus.Deleted, a.Status));
            Assert.Equal("connector.sync-aborted", Assert.Single(await RunEventsAsync(db, aborted)).Action);
        }

        var approved = await SyncAsync(allowMassDelete: true);
        Assert.Equal(ConnectorRunStatus.Succeeded, approved.Status);
        Assert.Equal(2, approved.AccountsDeleted);
        await using (var db = pg.CreateContext())
        {
            var aliyev = (await AccountsAsync(db)).Single(a => a.Login == "j.aliyev");
            Assert.Equal(AccountStatus.Deleted, aliyev.Status);
            Assert.Empty(aliyev.Entitlements);
            // AD manbali xodimning yagona akkaunti o'chirildi: ishdan ketgan
            Assert.Equal(IdentityStatus.Terminated, aliyev.Identity!.Status);
            Assert.NotNull(aliyev.Identity.TerminationDate);
        }
    }

    [Fact]
    public async Task Connector_failure_is_recorded_and_changes_nothing()
    {
        _connector.Error = new ConnectorException("ldaps://dc1:636 ga ulanib bo'lmadi");

        var run = await SyncAsync();

        Assert.Equal(ConnectorRunStatus.Failed, run.Status);
        Assert.Equal("ldaps://dc1:636 ga ulanib bo'lmadi", run.Error);
        await using var db = pg.CreateContext();
        Assert.Empty(await AccountsAsync(db));
        Assert.Equal("connector.sync-failed", Assert.Single(await RunEventsAsync(db, run)).Action);
    }

    [Fact]
    public async Task Identities_from_hr_or_manual_entry_are_linked_but_not_overwritten()
    {
        await using (var db = pg.CreateContext())
        {
            db.Identities.Add(new Identity { EmployeeNumber = $"{Prefix}-TAB-1", FirstName = "Aziz", LastName = "Karimov", JobTitle = "HR'dagi lavozim", Source = IdentitySource.Hr });
            await db.SaveChangesAsync(Ct);
        }
        SetDirectory();

        await SyncAsync();

        await using (var db = pg.CreateContext())
        {
            var karimov = (await AccountsAsync(db)).Single(a => a.Login == "a.karimov");
            Assert.Equal(IdentitySource.Hr, karimov.Identity!.Source);
            Assert.Equal("HR'dagi lavozim", karimov.Identity.JobTitle);
            Assert.Null(karimov.Identity.ManagerId);
        }
    }
}
