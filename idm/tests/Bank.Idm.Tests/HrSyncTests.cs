using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Hr;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Tests;

public class FakeHrSource : IHrSource
{
    public HrSnapshot Snapshot { get; set; } = new([]);
    public Task<HrSnapshot> ReadAsync(CancellationToken ct = default) => Task.FromResult(Snapshot);
}

public class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>HR yagona haqiqat manbai (barcha HR xodimlari unga tegishli), shuning uchun har bir test o'z bazasida.</summary>
public class HrSyncTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    // Toshkentda 05.03.2026 01:00, UTC bo'yicha hali 04.03
    private static readonly TimeProvider Clock = new FixedTime(new DateTimeOffset(2026, 3, 4, 20, 0, 0, TimeSpan.Zero));
    private readonly FakeHrSource _hr = new();
    private readonly string _p = Guid.NewGuid().ToString("N")[..8];
    private Guid _hrId, _adId;

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using var db = pg.CreateContext();
        var hr = new Application { Code = $"HR{_p}", Name = "HR", Kind = ApplicationKind.HumanResources };
        var ad = new Application { Code = $"AD{_p}", Name = "AD", Kind = ApplicationKind.Directory };
        db.Applications.AddRange(hr, ad);
        await db.SaveChangesAsync(Ct);
        (_hrId, _adId) = (hr.Id, ad.Id);
    }

    public ValueTask DisposeAsync() => pg.DisposeAsync();

    private string N(string n) => $"{_p}-{n}";
    private HrRecord Karimov => new(N("1"), "Aziz", "Karimov", Department: "Kredit bo'limi", JobTitle: "Mutaxassis", ManagerEmployeeNumber: N("2"));
    private HrRecord Rahimova => new(N("2"), "Dilnoza", "Rahimova", Department: "Kredit bo'limi", JobTitle: "Boshliq");
    private HrRecord Aliyev => new(N("3"), "Jasur", "Aliyev", Department: "IT");
    private HrRecord Yusupova => new(N("4"), "Nodira", "Yusupova", Department: "Buxgalteriya");
    private HrRecord Qodirov => new(N("5"), "Murod", "Qodirov", Department: "IT");
    private void SetHr(params HrRecord[] records) => _hr.Snapshot = new HrSnapshot(records);

    private async Task<ConnectorRun> SyncAsync(HrSettings? settings = null, bool allowMassDelete = false)
    {
        await using var db = pg.CreateContext();
        var sync = new HrSync(db, new AuditLog(db, Clock), Clock);
        var run = (await sync.StartRunAsync(_hrId, ConnectorRunTrigger.Manual, "boss", ct: Ct))!;
        return await sync.ExecuteAsync(run.Id, _hr, settings ?? new HrSettings(MaxDeletePercent: 30), "Boss", allowMassDelete, Ct);
    }

    private async Task<Identity> GetAsync(IdmDbContext db, string number) => await db.Identities.SingleAsync(i => i.EmployeeNumber == N(number), Ct);
    private async Task<List<IdentityEventKind>> EventsAsync(IdmDbContext db, string number) =>
        await db.IdentityEvents.Where(e => e.Identity.EmployeeNumber == N(number)).OrderBy(e => e.Id).Select(e => e.Kind).ToListAsync(Ct);

    [Fact]
    public async Task First_load_creates_hr_identities_with_managers_and_no_jml_events()
    {
        SetHr(Karimov, Rahimova, Aliyev);

        var run = await SyncAsync();

        Assert.Equal((ConnectorRunStatus.Succeeded, 3, 3), (run.Status, run.AccountsRead, run.IdentitiesCreated));
        await using var db = pg.CreateContext();
        var karimov = await GetAsync(db, "1");
        Assert.Equal((IdentitySource.Hr, IdentityStatus.Active), (karimov.Source, karimov.Status));
        Assert.Equal((await GetAsync(db, "2")).Id, karimov.ManagerId);
        Assert.Empty(await EventsAsync(db, "1"));
    }

    [Fact]
    public async Task Hr_takes_over_an_ad_sourced_identity_and_ad_no_longer_overwrites_it()
    {
        await using (var db = pg.CreateContext())
        {
            db.Identities.Add(new Identity { EmployeeNumber = N("1"), FirstName = "Aziz", LastName = "Karimov", JobTitle = "AD'dagi lavozim", Source = IdentitySource.ActiveDirectory });
            await db.SaveChangesAsync(Ct);
        }
        SetHr(Karimov, Rahimova);
        await SyncAsync();

        // AD sinxronlash keyin boshqa lavozim bilan keladi: HR manbali xodimga tegilmaydi
        await using (var db = pg.CreateContext())
        {
            var connector = new FakeConnector { Snapshot = new ConnectorSnapshot([new ExternalAccount("x-1", "a.karimov", true, N("1"), JobTitle: "Boshqa")], []) };
            var sync = new ConnectorSync(db, new AuditLog(db, Clock), Clock);
            var adRun = (await sync.StartRunAsync(_adId, ConnectorRunTrigger.Manual, "boss", ct: Ct))!;
            await sync.ExecuteAsync(adRun.Id, connector, new SyncSettings(), "Boss", ct: Ct);
        }
        await using (var db = pg.CreateContext())
        {
            var karimov = await GetAsync(db, "1");
            Assert.Equal((IdentitySource.Hr, "Mutaxassis"), (karimov.Source, karimov.JobTitle));
            Assert.Equal(karimov.Id, (await db.Accounts.SingleAsync(a => a.ApplicationId == _adId, Ct)).IdentityId);
        }
    }

    [Fact]
    public async Task Movers_leavers_joiners_and_rehires_are_recorded_as_jml_events()
    {
        SetHr(Karimov, Rahimova, Aliyev, Yusupova with { TerminationDate = new DateOnly(2025, 12, 31) }, Qodirov);
        await SyncAsync();

        SetHr(
            Karimov with { Department = "Riskler bo'limi" },                         // mover
            Rahimova,
            Aliyev with { TerminationDate = new DateOnly(2026, 3, 5) },             // Toshkent bo'yicha bugun: leaver
            Yusupova with { TerminationDate = null, HireDate = new DateOnly(2026, 3, 1) }, // qayta ishga olindi
            Qodirov with { TerminationDate = new DateOnly(2026, 3, 6) },            // ertaga ketadi: hali ishlaydi
            new HrRecord(N("6"), "Zarina", "Ergasheva", HireDate: new DateOnly(2026, 4, 1)));   // kelgusi oyda keladi
        var run = await SyncAsync();

        Assert.Equal((1, 1), (run.IdentitiesCreated, run.IdentitiesTerminated));
        await using var db = pg.CreateContext();
        Assert.Equal([IdentityEventKind.Mover], await EventsAsync(db, "1"));
        Assert.Empty(await EventsAsync(db, "2"));
        Assert.Equal([IdentityEventKind.Leaver], await EventsAsync(db, "3"));
        Assert.Equal(IdentityStatus.Terminated, (await GetAsync(db, "3")).Status);
        Assert.Equal([IdentityEventKind.Rehire], await EventsAsync(db, "4"));
        Assert.Null((await GetAsync(db, "4")).TerminationDate);
        Assert.Equal(IdentityStatus.Active, (await GetAsync(db, "5")).Status);
        Assert.Equal(IdentityStatus.Pending, (await GetAsync(db, "6")).Status);
        Assert.Equal([IdentityEventKind.Joiner], await EventsAsync(db, "6"));
        Assert.True((await new AuditLog(db, Clock).VerifyChainAsync(Ct)).IsValid);
    }

    [Fact]
    public async Task Missing_employees_are_only_reported_unless_terminate_missing_is_enabled()
    {
        SetHr(Karimov, Rahimova, Aliyev, Yusupova);
        await SyncAsync();
        SetHr(Karimov, Rahimova, Aliyev);

        var reported = await SyncAsync();
        Assert.Equal((1, 0), (reported.IdentitiesMissing, reported.IdentitiesTerminated));
        Assert.Contains(N("4"), reported.Error);
        await using (var db = pg.CreateContext()) Assert.Equal(IdentityStatus.Active, (await GetAsync(db, "4")).Status);

        var terminated = await SyncAsync(new HrSettings(MaxDeletePercent: 30, TerminateMissing: true));
        Assert.Equal(1, terminated.IdentitiesTerminated);
        await using (var db = pg.CreateContext())
        {
            var yusupova = await GetAsync(db, "4");
            Assert.Equal((IdentityStatus.Terminated, new DateOnly(2026, 3, 5)), (yusupova.Status, yusupova.TerminationDate));
            Assert.Equal([IdentityEventKind.Leaver], await EventsAsync(db, "4"));
        }
    }

    [Fact]
    public async Task Rows_skipped_for_bad_data_are_not_treated_as_missing()
    {
        SetHr(Karimov, Rahimova);
        await SyncAsync();
        _hr.Snapshot = new HrSnapshot([Karimov]) { SkippedEmployeeNumbers = new HashSet<string> { N("2") }, Warnings = ["3-qator: sana noto'g'ri"] };

        var run = await SyncAsync(new HrSettings(MaxDeletePercent: 100, TerminateMissing: true));

        Assert.Equal((0, 0), (run.IdentitiesMissing, run.IdentitiesTerminated));
        await using var db = pg.CreateContext();
        Assert.Equal(IdentityStatus.Active, (await GetAsync(db, "2")).Status);
    }

    [Fact]
    public async Task Mass_termination_is_aborted_until_approved()
    {
        SetHr(Karimov, Rahimova, Aliyev);
        await SyncAsync();
        var gone = new DateOnly(2026, 1, 1);
        SetHr(Karimov with { TerminationDate = gone }, Rahimova with { TerminationDate = gone }, Aliyev);

        var aborted = await SyncAsync();
        Assert.Equal(ConnectorRunStatus.Aborted, aborted.Status);
        await using (var db = pg.CreateContext()) Assert.Equal(IdentityStatus.Active, (await GetAsync(db, "1")).Status);

        var approved = await SyncAsync(allowMassDelete: true);
        Assert.Equal((ConnectorRunStatus.Succeeded, 2), (approved.Status, approved.IdentitiesTerminated));
    }
}
