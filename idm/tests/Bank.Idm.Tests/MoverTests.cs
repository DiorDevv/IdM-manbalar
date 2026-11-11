using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Hr;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Bank.Idm.Infrastructure.Requests;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Tests;

/// <summary>
/// O'tish (mover): filial yoki bo'lim o'zgarganda ariza/qo'lda berilgan rollar yangi rahbar qaroriga qo'yiladi, muddat o'tgach tugaydi.
/// HR yagona haqiqat manbai bo'lgani uchun har bir test o'z bazasida.
/// </summary>
public class MoverTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Start = new(2026, 3, 4, 6, 0, 0, TimeSpan.Zero);
    private readonly MutableTime _clock = new(Start);
    private readonly FakeHrSource _hr = new();
    private readonly string _p = Guid.NewGuid().ToString("N")[..8];
    private Guid _hrId, _roleId, _role2Id, _birthId;

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using var db = pg.CreateContext();
        var hr = new Application { Code = $"HR{_p}", Name = "HR", Kind = ApplicationKind.HumanResources };
        var role = new Role { Code = $"KREDIT{_p}", Name = "Kredit tizimi" };
        var role2 = new Role { Code = $"KASSA{_p}", Name = "Kassa" };
        var birth = new Role { Code = $"BOSH{_p}", Name = "Filial papkasi" };
        birth.Rules.Add(new RoleRule { Branch = "Chilonzor", CreatedBy = "boss" });
        db.AddRange(hr, role, role2, birth);
        await db.SaveChangesAsync(Ct);
        (_hrId, _roleId, _role2Id, _birthId) = (hr.Id, role.Id, role2.Id, birth.Id);
    }

    public ValueTask DisposeAsync() => pg.DisposeAsync();

    private string N(string n) => $"{_p}-{n}";
    // 1 — o'tadigan xodim; 2 — eski rahbar (Kredit); 3 — yangi rahbar (Kassa); 4..8 — boshqalar (foiz chegarasi uchun)
    private HrRecord Karimov => new(N("1"), "Aziz", "Karimov", Department: "Kredit", JobTitle: "Mutaxassis", ManagerEmployeeNumber: N("2"), Branch: "Chilonzor");
    private HrRecord OldBoss => new(N("2"), "Dilnoza", "Rahimova", Department: "Kredit", JobTitle: "Boshliq", Branch: "Chilonzor");
    private HrRecord NewBoss => new(N("3"), "Jasur", "Aliyev", Department: "Kassa", JobTitle: "Boshliq", Branch: "Yunusobod");
    private IEnumerable<HrRecord> Others => Enumerable.Range(4, 5).Select(i => new HrRecord(N(i.ToString()), "Ism", $"Familiya{i}", Department: "IT", Branch: "Bosh ofis"));
    private void SetHr(bool hasBranch, params HrRecord[] records) => _hr.Snapshot = new HrSnapshot([.. records, .. Others]) { HasBranch = hasBranch };

    private async Task<ConnectorRun> SyncAsync(int? grace = 7, bool allowMass = false, int maxMove = 20)
    {
        await using var db = pg.CreateContext();
        var sync = new HrSync(db, new AuditLog(db, _clock), _clock);
        var run = (await sync.StartRunAsync(_hrId, ConnectorRunTrigger.Manual, "boss", ct: Ct))!;
        return await sync.ExecuteAsync(run.Id, _hr, new HrSettings(MaxDeletePercent: 50, MoverGraceDays: grace, MaxMovePercent: maxMove), "Boss", allowMass, Ct);
    }

    private async Task<Identity> GetAsync(IdmDbContext db, string n) => await db.Identities.SingleAsync(i => i.EmployeeNumber == N(n), Ct);

    /// <summary>Boshlang'ich holat: HR yuklangan, Karimovda ariza roli (+ muddatli qo'lda berilgan rol) va kutilayotgan ariza.</summary>
    private async Task<(Guid Request, Guid Manual, Guid Pending)> SeedAsync(DateTimeOffset? manualValidTo = null)
    {
        SetHr(true, Karimov, OldBoss, NewBoss);
        await SyncAsync();
        await using var db = pg.CreateContext();
        var k = await GetAsync(db, "1");
        var request = new IdentityRole { IdentityId = k.Id, RoleId = _roleId, Source = RoleAssignmentSource.Request, ValidFrom = Start.AddDays(-30) };
        var manual = new IdentityRole { IdentityId = k.Id, RoleId = _role2Id, Source = RoleAssignmentSource.Manual, ValidFrom = Start.AddDays(-30), ValidTo = manualValidTo };
        var pending = new AccessRequest { RequesterId = k.Id, BeneficiaryId = k.Id, RoleId = _role2Id, Justification = "eski bo'lim uchun", CreatedAt = Start };
        db.AddRange(request, manual, pending);
        await db.SaveChangesAsync(Ct);
        await new BirthrightService(db, new AuditLog(db, _clock), _clock).RecalculateAsync("boss", "Boss", Ct);
        return (request.Id, manual.Id, pending.Id);
    }

    private HrRecord Moved => Karimov with { Department = "Kassa", ManagerEmployeeNumber = N("3"), Branch = "Yunusobod" };

    [Fact]
    public async Task Moving_to_another_department_puts_requested_roles_on_hold_and_cancels_pending_requests()
    {
        var (request, manual, pending) = await SeedAsync();

        SetHr(true, Moved, OldBoss, NewBoss);
        var run = await SyncAsync();

        Assert.Equal(ConnectorRunStatus.Succeeded, run.Status);
        await using var db = pg.CreateContext();
        var roles = await db.IdentityRoles.Where(r => r.Identity.EmployeeNumber == N("1")).ToListAsync(Ct);
        Assert.Equal(Start.AddDays(7), roles.Single(r => r.Id == request).MoverDueAt);
        Assert.Equal(Start.AddDays(7), roles.Single(r => r.Id == manual).MoverDueAt);
        Assert.All(roles.Where(r => r.Id == request || r.Id == manual), r => Assert.Null(r.ValidTo));
        // Avtomatik rol (filial qoidasi) qarorga qo'yilmaydi: qoidadan qayta hisoblanadi
        var birth = roles.Single(r => r.RoleId == _birthId);
        Assert.Null(birth.MoverDueAt);
        Assert.Equal(AccessRequestStatus.Cancelled, (await db.AccessRequests.SingleAsync(r => r.Id == pending, Ct)).Status);
        var k = await GetAsync(db, "1");
        Assert.Equal(("Yunusobod", "Kassa"), (k.Branch, k.Department));
        Assert.Contains(IdentityEventKind.Mover, await db.IdentityEvents.Where(e => e.IdentityId == k.Id).Select(e => e.Kind).ToListAsync(Ct));
        Assert.Equal(2, await db.AuditEvents.CountAsync(e => e.Action == "identity.role-mover-pending", Ct));
        Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "request.cancelled" && e.Details!.Contains("beneficiary-moved"), Ct));
    }

    [Fact]
    public async Task Job_title_manager_or_spelling_changes_are_not_moves()
    {
        var (request, _, pending) = await SeedAsync();

        SetHr(true, Karimov with { JobTitle = "Bosh mutaxassis", ManagerEmployeeNumber = N("3"), Department = " kredit ", Branch = "CHILONZOR" }, OldBoss, NewBoss);
        await SyncAsync();

        await using var db = pg.CreateContext();
        Assert.Null((await db.IdentityRoles.SingleAsync(r => r.Id == request, Ct)).MoverDueAt);
        Assert.Equal(AccessRequestStatus.Pending, (await db.AccessRequests.SingleAsync(r => r.Id == pending, Ct)).Status);
    }

    [Fact]
    public async Task Source_without_branch_keeps_branch_and_does_not_trigger_a_move()
    {
        var (request, _, _) = await SeedAsync();

        SetHr(false, Karimov with { Branch = null }, OldBoss, NewBoss);
        await SyncAsync();

        await using var db = pg.CreateContext();
        Assert.Equal("Chilonzor", (await GetAsync(db, "1")).Branch);
        Assert.Null((await db.IdentityRoles.SingleAsync(r => r.Id == request, Ct)).MoverDueAt);
    }

    [Fact]
    public async Task Branch_change_alone_is_a_move()
    {
        var (request, _, _) = await SeedAsync();

        SetHr(true, Karimov with { Branch = "Yunusobod" }, OldBoss, NewBoss);
        await SyncAsync();

        await using var db = pg.CreateContext();
        Assert.NotNull((await db.IdentityRoles.SingleAsync(r => r.Id == request, Ct)).MoverDueAt);
    }

    [Fact]
    public async Task Zero_grace_days_end_roles_immediately_and_disabled_policy_leaves_them()
    {
        var (request, _, _) = await SeedAsync();
        SetHr(true, Karimov with { Department = "Kassa" }, OldBoss, NewBoss);
        await SyncAsync(grace: null);
        await using (var db = pg.CreateContext())
        {
            var r = await db.IdentityRoles.SingleAsync(x => x.Id == request, Ct);
            Assert.Equal((null, (DateTimeOffset?)null), (r.MoverDueAt, r.ValidTo));
        }

        SetHr(true, Karimov with { Department = "Kassa", Branch = "Yunusobod" }, OldBoss, NewBoss);
        await SyncAsync(grace: 0);
        await using (var db = pg.CreateContext())
        {
            var r = await db.IdentityRoles.SingleAsync(x => x.Id == request, Ct);
            Assert.Equal((null, (DateTimeOffset?)Start), (r.MoverDueAt, r.ValidTo));
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "identity.role-removed" && e.Details!.Contains("\"reason\":\"mover\""), Ct));
        }
    }

    [Fact]
    public async Task A_role_expiring_before_the_deadline_needs_no_decision_and_a_second_move_does_not_extend_it()
    {
        var (request, manual, _) = await SeedAsync(manualValidTo: Start.AddDays(3));

        SetHr(true, Moved, OldBoss, NewBoss);
        await SyncAsync();
        _clock.Now = Start.AddDays(2);
        SetHr(true, Moved with { Department = "Riskler" }, OldBoss, NewBoss);
        await SyncAsync();

        await using var db = pg.CreateContext();
        Assert.Null((await db.IdentityRoles.SingleAsync(r => r.Id == manual, Ct)).MoverDueAt);
        Assert.Equal(Start.AddDays(7), (await db.IdentityRoles.SingleAsync(r => r.Id == request, Ct)).MoverDueAt);
    }

    [Fact]
    public async Task Mass_move_is_refused_unless_confirmed()
    {
        await SeedAsync();
        // 8 ishlayotgandan 5 tasi (IT → Kredit) — 62%
        _hr.Snapshot = new HrSnapshot([Karimov, OldBoss, NewBoss, .. Others.Select(o => o with { Department = "Kredit" })]) { HasBranch = true };

        var run = await SyncAsync();
        Assert.Equal(ConnectorRunStatus.Aborted, run.Status);
        Assert.Contains("boshqa filial yoki bo'limga", run.Error);
        await using (var db = pg.CreateContext())
            Assert.Equal("IT", (await GetAsync(db, "4")).Department);

        Assert.Equal(ConnectorRunStatus.Succeeded, (await SyncAsync(allowMass: true)).Status);
    }

    private async Task<(RequestActor Employee, RequestActor NewBoss, RequestActor OldBoss, RequestActor Admin)> ActorsAsync()
    {
        await using var db = pg.CreateContext();
        var (k, o, n) = (await GetAsync(db, "1"), await GetAsync(db, "2"), await GetAsync(db, "3"));
        return (new RequestActor("k", "K", k.Id, false), new RequestActor("n", "N", n.Id, false), new RequestActor("o", "O", o.Id, false),
            new RequestActor("admin", "Admin", null, true));
    }

    private async Task<string?> DecideAsync(RequestActor actor, Guid id, MoverDecision decision, string? comment = "yangi joyda ham kerak")
    {
        await using var db = pg.CreateContext();
        var result = await new MoverService(db, new AuditLog(db, _clock), _clock).DecideAsync(actor, id, decision, comment, Ct);
        return result.Value is null ? $"{result.Status}" : null;
    }

    [Fact]
    public async Task Only_the_new_manager_their_delegate_or_an_admin_decides_and_only_once()
    {
        var (request, manual, _) = await SeedAsync();
        SetHr(true, Moved, OldBoss, NewBoss);
        await SyncAsync();
        var (employee, newBoss, oldBoss, admin) = await ActorsAsync();

        Assert.Equal("403", await DecideAsync(employee, request, MoverDecision.Keep));      // o'z roli
        Assert.Equal("403", await DecideAsync(oldBoss, request, MoverDecision.Keep));       // eski rahbar endi emas
        Assert.Equal("400", await DecideAsync(newBoss, request, MoverDecision.Keep, " "));  // qoldirish — asos bilan
        var delegateOfNew = oldBoss with { ActingFor = [newBoss.IdentityId!.Value] };
        Assert.Null(await DecideAsync(delegateOfNew, request, MoverDecision.Keep));        // yangi rahbarning o'rinbosari
        Assert.Equal("409", await DecideAsync(newBoss, request, MoverDecision.Revoke));    // allaqachon hal qilingan
        Assert.Null(await DecideAsync(admin, manual, MoverDecision.Revoke, null));

        await using var db = pg.CreateContext();
        var kept = await db.IdentityRoles.SingleAsync(r => r.Id == request, Ct);
        Assert.Equal((null, (DateTimeOffset?)null), (kept.MoverDueAt, kept.ValidTo));
        var revoked = await db.IdentityRoles.SingleAsync(r => r.Id == manual, Ct);
        Assert.Equal((null, (DateTimeOffset?)Start), (revoked.MoverDueAt, revoked.ValidTo));
        var keptAudit = await db.AuditEvents.SingleAsync(e => e.Action == "identity.role-mover-kept", Ct);
        Assert.Contains(newBoss.IdentityId!.Value.ToString(), keptAudit.Details);   // kimning o'rniga
    }

    [Fact]
    public async Task Undecided_roles_end_after_the_deadline_and_decided_ones_stay()
    {
        var (request, manual, _) = await SeedAsync();
        SetHr(true, Moved, OldBoss, NewBoss);
        await SyncAsync();
        var (_, newBoss, _, _) = await ActorsAsync();
        Assert.Null(await DecideAsync(newBoss, request, MoverDecision.Keep));

        await using (var db = pg.CreateContext())
            Assert.Equal(0, await new MoverService(db, new AuditLog(db, _clock), _clock).ExpireAsync("system", null, Ct));

        _clock.Now = Start.AddDays(7).AddMinutes(1);
        await using (var db = pg.CreateContext())
        {
            Assert.Equal(1, await new MoverService(db, new AuditLog(db, _clock), _clock).ExpireAsync("system", null, Ct));
            Assert.Equal("400", await DecideAsync(newBoss, manual, MoverDecision.Keep, "kech"));   // asos qisqa
            Assert.Equal("409", await DecideAsync(newBoss, manual, MoverDecision.Keep));          // muddati o'tgan
        }
        await using (var db = pg.CreateContext())
        {
            Assert.Equal(_clock.Now, (await db.IdentityRoles.SingleAsync(r => r.Id == manual, Ct)).ValidTo);
            Assert.Null((await db.IdentityRoles.SingleAsync(r => r.Id == request, Ct)).ValidTo);
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "identity.role-removed" && e.Details!.Contains("mover-grace-expired"), Ct));
            Assert.True((await new AuditLog(db, _clock).VerifyChainAsync(Ct)).IsValid);
        }
    }

    [Fact]
    public async Task Leaver_loses_time_limited_roles_too_and_branch_rules_follow_the_branch()
    {
        var (_, manual, _) = await SeedAsync(manualValidTo: Start.AddDays(60));
        await using (var db = pg.CreateContext())
            Assert.True(await db.IdentityRoles.AnyAsync(r => r.RoleId == _birthId && r.Identity.EmployeeNumber == N("1") && r.ValidTo == null, Ct));

        SetHr(true, Karimov with { Status = HrStatus.Terminated }, OldBoss, NewBoss);
        await SyncAsync();
        await using (var db = pg.CreateContext())
            await new BirthrightService(db, new AuditLog(db, _clock), _clock).RecalculateAsync("boss", "Boss", Ct);

        await using (var db = pg.CreateContext())
        {
            Assert.Equal(Start, (await db.IdentityRoles.SingleAsync(r => r.Id == manual, Ct)).ValidTo);
            Assert.False(await db.IdentityRoles.AnyAsync(r => r.Identity.EmployeeNumber == N("1") && (r.ValidTo == null || r.ValidTo > Start), Ct));
            // Filial qoidasi: Chilonzordagi rahbarda bor, Yunusoboddagida yo'q
            Assert.True(await db.IdentityRoles.AnyAsync(r => r.RoleId == _birthId && r.Identity.EmployeeNumber == N("2") && r.ValidTo == null, Ct));
            Assert.False(await db.IdentityRoles.AnyAsync(r => r.RoleId == _birthId && r.Identity.EmployeeNumber == N("3"), Ct));
        }
    }
}
