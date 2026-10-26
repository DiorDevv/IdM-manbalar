using Bank.Idm.Connectors;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Provisioning;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Tests;

/// <summary>Xotiradagi katalog: yozish amallarini qayd etadi, kerak bo'lsa xato qaytaradi.</summary>
public sealed class FakeProvisioner : IProvisioningConnector
{
    public record Entry(string Id, string Login, string? EmployeeNumber, bool Enabled);
    public Dictionary<string, Entry> Accounts { get; } = new();
    public HashSet<(string Account, string Group)> Members { get; } = [];
    public List<string> Calls { get; } = [];
    /// <summary>Amal nomi (create, disable, add, remove) bo'yicha xato.</summary>
    public Func<string, Exception?> Fail { get; set; } = _ => null;

    public void Dispose() { }

    private void Call(string name, string args)
    {
        lock (Calls) Calls.Add($"{name} {args}");
        if (Fail(name) is { } e) throw e;
    }

    private static ExternalAccount ToExternal(Entry e) => new(e.Id, e.Login, e.Enabled, e.EmployeeNumber);

    public Task<ExternalAccount?> FindByLoginAsync(string login, CancellationToken ct = default) =>
        Task.FromResult(Accounts.Values.Where(a => a.Login == login).Select(ToExternal).FirstOrDefault());

    public Task<ExternalAccount?> FindByEmployeeNumberAsync(string employeeNumber, CancellationToken ct = default) =>
        Task.FromResult(Accounts.Values.Where(a => a.EmployeeNumber == employeeNumber).Select(ToExternal).FirstOrDefault());

    public Task<ExternalAccount> CreateAccountAsync(NewAccount account, CancellationToken ct = default)
    {
        Call("create", account.Login);
        var entry = new Entry($"new-{account.Login}", account.Login, account.EmployeeNumber, false);
        lock (Accounts) Accounts[entry.Id] = entry;
        return Task.FromResult(ToExternal(entry) with { DisplayName = account.DisplayName, DistinguishedName = $"uid={account.Login},ou=test" });
    }

    public Task<bool> DisableAccountAsync(string accountId, CancellationToken ct = default)
    {
        Call("disable", accountId);
        lock (Accounts)
        {
            var was = !Accounts.TryGetValue(accountId, out var e) || e.Enabled;
            if (e is not null) Accounts[accountId] = e with { Enabled = false };
            return Task.FromResult(was);
        }
    }

    public Task<bool> AddMemberAsync(string accountId, string groupId, CancellationToken ct = default)
    {
        Call("add", $"{accountId} {groupId}");
        lock (Members) return Task.FromResult(Members.Add((accountId, groupId)));
    }

    public Task<bool> RemoveMemberAsync(string accountId, string groupId, CancellationToken ct = default)
    {
        Call("remove", $"{accountId} {groupId}");
        lock (Members) return Task.FromResult(Members.Remove((accountId, groupId)));
    }
}

public sealed class MutableTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Tizimga yozish. Bank holati: Karimov — ishlaydi, "KREDIT" roli bor, lekin AD'da kredit guruhi yo'q (qo'lda berilgan ABS guruhi bor);
/// Rahimova — ishdan ketgan, akkaunti faol, kredit va Domain Admins guruhlarida, yana xizmat akkaunti va himoyalangan "administrator" bor;
/// Yusupova — vaqtincha to'xtatilgan; Aliyev — ishlaydi, KREDIT roli bor, AD'da akkaunti yo'q.
/// </summary>
public class ProvisioningTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    // PostgreSQL vaqtni mikrosekund aniqligida saqlaydi
    private readonly MutableTime _time = new(new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero));
    private readonly FakeProvisioner _directory = new();
    private Guid _app, _karimov, _rahimova, _yusupova, _aliyev, _karimovAcc, _rahimovaAcc, _yusupovaAcc, _credit, _abs, _admins, _kreditRole;

    private static readonly ProvisioningSettings Approval = new(ProvisioningMode.Approval, ProtectedLogins: ["administrator"]);

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using var db = pg.CreateContext();
        var ad = new Application { Code = "AD", Name = "Active Directory", Kind = ApplicationKind.Directory };
        Entitlement Group(string id, string name, bool privileged = false) => new() { Application = ad, ExternalId = id, Name = name, IsPrivileged = privileged };
        var credit = Group("g-credit", "kredit-bolimi");
        var abs = Group("g-abs", "abs-operatorlar");
        var admins = Group("g-admins", "Domain Admins", privileged: true);
        Identity Person(string number, string first, string last, IdentityStatus status) =>
            new() { EmployeeNumber = number, FirstName = first, LastName = last, Status = status, Source = IdentitySource.Hr };
        var karimov = Person("T1", "Aziz", "Karimov", IdentityStatus.Active);
        var rahimova = Person("T2", "Dilnoza", "Rahimova", IdentityStatus.Terminated);
        var yusupova = Person("T3", "Nodira", "Yusupova", IdentityStatus.Suspended);
        var aliyev = Person("T4", "Jasur", "Aliyev", IdentityStatus.Active);
        Account Acc(Identity i, string login, AccountKind kind = AccountKind.Personal, params Entitlement[] groups)
        {
            var a = new Account { Application = ad, Identity = i, ExternalId = $"u-{login}", Login = login, Kind = kind, EmployeeNumber = i.EmployeeNumber };
            foreach (var g in groups) a.Entitlements.Add(new AccountEntitlement { Entitlement = g });
            _directory.Accounts[a.ExternalId] = new FakeProvisioner.Entry(a.ExternalId, login, i.EmployeeNumber, true);
            foreach (var g in groups) _directory.Members.Add((a.ExternalId, g.ExternalId));
            return a;
        }
        var karimovAcc = Acc(karimov, "a.karimov", AccountKind.Personal, abs);
        var rahimovaAcc = Acc(rahimova, "d.rahimova", AccountKind.Personal, credit, admins);
        var yusupovaAcc = Acc(yusupova, "n.yusupova");
        var role = new Role { Code = "KREDIT", Name = "Kredit mutaxassisi" };
        role.Entitlements.Add(new RoleEntitlement { Entitlement = credit });
        db.AddRange(ad, credit, abs, admins, karimov, rahimova, yusupova, aliyev, karimovAcc, rahimovaAcc, yusupovaAcc,
            Acc(rahimova, "svc-report", AccountKind.Service, credit), Acc(rahimova, "administrator", AccountKind.Privileged, admins), role,
            new IdentityRole { Identity = karimov, Role = role, Source = RoleAssignmentSource.Request, ValidFrom = _time.Now.AddDays(-1) },
            new IdentityRole { Identity = aliyev, Role = role, Source = RoleAssignmentSource.Birthright, ValidFrom = _time.Now.AddDays(-1) },
            new ConnectorRun { Application = ad, TriggeredBy = "system", Status = ConnectorRunStatus.Succeeded, StartedAt = _time.Now.AddMinutes(-5), FinishedAt = _time.Now.AddMinutes(-4) });
        await db.SaveChangesAsync(Ct);
        (_app, _karimov, _rahimova, _yusupova, _aliyev) = (ad.Id, karimov.Id, rahimova.Id, yusupova.Id, aliyev.Id);
        (_karimovAcc, _rahimovaAcc, _yusupovaAcc, _credit, _abs, _admins, _kreditRole) = (karimovAcc.Id, rahimovaAcc.Id, yusupovaAcc.Id, credit.Id, abs.Id, admins.Id, role.Id);
    }

    public async ValueTask DisposeAsync() => await pg.DisposeAsync();

    private async Task<PlanResult> PlanAsync(ProvisioningSettings? settings = null)
    {
        await using var db = pg.CreateContext();
        return await new ProvisioningPlanner(db, new AuditLog(db, _time), _time).PlanAsync(_app, settings ?? Approval, "boss", "Boss", Ct);
    }

    private async Task<ExecuteResult> ExecuteAsync(ProvisioningSettings? settings = null, IProvisioningConnector? connector = null)
    {
        await using var db = pg.CreateContext();
        return await new ProvisioningExecutor(db, new AuditLog(db, _time), _time).ExecuteDueAsync(_app, connector ?? _directory, settings ?? Approval, ct: Ct);
    }

    private async Task<ProvisioningActionResult> ActAsync(ProvisioningAction action, IEnumerable<Guid>? ids = null)
    {
        await using var db = pg.CreateContext();
        var targets = ids?.ToList() ?? await db.ProvisioningOperations.Select(o => o.Id).ToListAsync(Ct);
        return await new ProvisioningService(db, new AuditLog(db, _time), _time).ApplyAsync(action, targets, new HashSet<Guid> { _app }, "boss", "Boss", ct: Ct);
    }

    private async Task<List<ProvisioningOperation>> OperationsAsync()
    {
        await using var db = pg.CreateContext();
        return await db.ProvisioningOperations.AsNoTracking().OrderBy(o => o.Kind).ThenBy(o => o.EntitlementId).ToListAsync(Ct);
    }

    private static string Describe(ProvisioningOperation o) => $"{o.Kind} {o.AccountId?.ToString() ?? o.IdentityId.ToString()} {o.EntitlementId} {o.Reason}";

    [Fact]
    public async Task Approval_mode_is_a_dry_run_leaver_disabled_and_stripped_roles_granted_protected_and_service_accounts_untouched()
    {
        var plan = await PlanAsync();

        Assert.Null(plan.Skipped);
        Assert.Equal((5, 5, 0, false), (plan.Planned, plan.Created, plan.Obsoleted, plan.Held));
        Assert.Contains(plan.Warnings, w => w.StartsWith("T4:") && w.Contains("akkaunti yo'q"));
        var ops = await OperationsAsync();
        Assert.All(ops, o => Assert.Equal(ProvisioningStatus.Proposed, o.Status));
        Assert.Equal(new[]
        {
            $"DisableAccount {_rahimovaAcc}  leaver",
            $"DisableAccount {_yusupovaAcc}  suspended",
            $"AddMembership {_karimovAcc} {_credit} role",
            $"RemoveMembership {_rahimovaAcc} {_credit} leaver",
            $"RemoveMembership {_rahimovaAcc} {_admins} leaver",
        }.Order(), ops.Select(Describe).Order());
        Assert.Equal("KREDIT", ops.Single(o => o.Kind == ProvisioningKind.AddMembership).Summary);

        // Tasdiqlanmagan — tizimga hech narsa yozilmaydi; qayta reja — takror amal yo'q
        Assert.Equal(0, (await ExecuteAsync()).Succeeded);
        Assert.Empty(_directory.Calls);
        Assert.Equal((5, 0), ((await PlanAsync()).Planned, (await OperationsAsync()).Count - 5));
        await using var db = pg.CreateContext();
        Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.Action == "provisioning.planned", Ct));
    }

    [Fact]
    public async Task Approved_operations_are_written_to_the_directory_and_idm_state_follows()
    {
        await PlanAsync();
        Assert.Equal(5, (await ActAsync(ProvisioningAction.Approve)).Changed);

        var result = await ExecuteAsync();

        Assert.Equal(new ExecuteResult(5, 0, 0, 0), result);
        // Xavfsizlik birinchi: bloklash va chiqarish qo'shishdan oldin
        Assert.StartsWith("disable", _directory.Calls[0]);
        Assert.StartsWith("add", _directory.Calls[^1]);
        Assert.False(_directory.Accounts["u-d.rahimova"].Enabled);
        Assert.False(_directory.Accounts["u-n.yusupova"].Enabled);
        Assert.True(_directory.Accounts["u-administrator"].Enabled);
        Assert.Contains(("u-a.karimov", "g-credit"), _directory.Members);
        Assert.Contains(("u-svc-report", "g-credit"), _directory.Members);
        Assert.DoesNotContain(_directory.Members, m => m.Account == "u-d.rahimova");

        await using var db = pg.CreateContext();
        Assert.Equal(AccountStatus.Disabled, (await db.Accounts.SingleAsync(a => a.Id == _rahimovaAcc, Ct)).Status);
        Assert.False(await db.AccountEntitlements.AnyAsync(ae => ae.AccountId == _rahimovaAcc, Ct));
        Assert.True(await db.AccountEntitlements.AnyAsync(ae => ae.AccountId == _karimovAcc && ae.EntitlementId == _credit, Ct));
        Assert.All(await OperationsAsync(), o => Assert.Equal((ProvisioningStatus.Succeeded, 1, "boss"), (o.Status, o.Attempts, o.ApprovedBy)));
        Assert.Equal(5, await db.AuditEvents.CountAsync(e => e.Action == "provisioning.approved", Ct));
        var disabled = await db.AuditEvents.SingleAsync(e => e.Action == "provisioning.account-disabled" && e.TargetId == _rahimovaAcc.ToString(), Ct);
        Assert.Contains("\"approvedBy\":\"boss\"", disabled.Details);
        Assert.True((await new AuditLog(db, _time).VerifyChainAsync(Ct)).IsValid);

        // Holat yetib keldi: yangi reja bo'sh
        var again = await PlanAsync();
        Assert.Equal((0, 0), (again.Planned, again.Created));
    }

    [Fact]
    public async Task Operation_that_is_no_longer_needed_is_obsoleted_before_execution()
    {
        await PlanAsync();
        await ActAsync(ProvisioningAction.Approve);
        await using (var db = pg.CreateContext())
        {
            var assignment = await db.IdentityRoles.SingleAsync(r => r.IdentityId == _karimov, Ct);
            assignment.ValidTo = _time.Now.AddMinutes(-1);
            await db.SaveChangesAsync(Ct);
        }

        var plan = await PlanAsync();
        await ExecuteAsync();

        Assert.Equal(1, plan.Obsoleted);
        Assert.DoesNotContain(_directory.Calls, c => c.StartsWith("add"));
        Assert.Equal(ProvisioningStatus.Obsolete, (await OperationsAsync()).Single(o => o.Kind == ProvisioningKind.AddMembership).Status);
    }

    [Fact]
    public async Task Role_removal_takes_away_only_memberships_idm_itself_granted()
    {
        await PlanAsync();
        await ActAsync(ProvisioningAction.Approve);
        await ExecuteAsync();
        // Karimovning roli tugadi: IdM qo'shgan kredit guruhi olinadi, qo'lda berilgan ABS guruhi qoladi (attestatsiyaga)
        await using (var db = pg.CreateContext())
        {
            (await db.IdentityRoles.SingleAsync(r => r.IdentityId == _karimov, Ct)).ValidTo = _time.Now;
            await db.SaveChangesAsync(Ct);
        }

        await PlanAsync();

        var removal = (await OperationsAsync()).Single(o => o.Status == ProvisioningStatus.Proposed);
        Assert.Equal((ProvisioningKind.RemoveMembership, _karimovAcc, _credit, "role-removed"), (removal.Kind, removal.AccountId, removal.EntitlementId, removal.Reason));
    }

    [Fact]
    public async Task Automatic_mode_executes_at_once_but_holds_everything_for_approval_above_the_limit()
    {
        var plan = await PlanAsync(Approval with { Mode = ProvisioningMode.Automatic, MaxAutoOperations = 4 });

        Assert.True(plan.Held);
        Assert.All(await OperationsAsync(), o => Assert.Equal(ProvisioningStatus.Proposed, o.Status));
        await using (var db = pg.CreateContext())
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "provisioning.held", Ct));

        await using (var db = pg.CreateContext())
            await db.ProvisioningOperations.ExecuteDeleteAsync(Ct);
        var automatic = Approval with { Mode = ProvisioningMode.Automatic, MaxAutoOperations = 5 };
        Assert.False((await PlanAsync(automatic)).Held);
        Assert.Equal(5, (await ExecuteAsync(automatic)).Succeeded);
    }

    [Fact]
    public async Task Transient_error_is_retried_with_backoff_and_stops_the_batch_permanent_error_fails_at_once()
    {
        await PlanAsync();
        await ActAsync(ProvisioningAction.Approve);
        _directory.Fail = name => name == "disable" ? new ConnectorException("server band", transient: true) : null;

        var first = await ExecuteAsync();

        Assert.Equal((0, 1), (first.Succeeded, first.Retrying));
        Assert.Single(_directory.Calls);   // to'plam to'xtadi: qolgan amallar urinishini sarflamadi
        var retrying = (await OperationsAsync()).Single(o => o.Attempts == 1);
        Assert.Equal((ProvisioningStatus.Queued, "server band"), (retrying.Status, retrying.LastError));
        Assert.Equal(_time.Now.AddMinutes(1), retrying.NextAttemptAt);

        // Muddat kelmaguncha qayta urinilmaydi, keyin — doimiy xato: darhol "xato"
        _directory.Fail = name => name == "disable" ? new ConnectorException("huquq yetarli emas") : null;
        await ExecuteAsync();
        Assert.Equal(ProvisioningStatus.Queued, (await OperationsAsync()).Single(o => o.Id == retrying.Id).Status);
        _time.Now = _time.Now.AddMinutes(2);
        await ExecuteAsync();

        var ops = await OperationsAsync();
        var failed = ops.Where(o => o.Status == ProvisioningStatus.Failed).ToList();
        Assert.Equal(2, failed.Count);   // ikkala bloklash
        Assert.Equal(3, ops.Count(o => o.Status == ProvisioningStatus.Succeeded));
        await using (var db = pg.CreateContext())
            Assert.Equal(2, await db.AuditEvents.CountAsync(e => e.Action == "provisioning.failed", Ct));

        // Xato amal qayta rejada takrorlanmaydi; administrator qayta ishga tushiradi
        Assert.Equal(0, (await PlanAsync()).Created);
        _directory.Fail = _ => null;
        Assert.Equal(2, (await ActAsync(ProvisioningAction.Retry, failed.Select(f => f.Id))).Changed);
        Assert.Equal(2, (await ExecuteAsync()).Succeeded);
    }

    [Fact]
    public async Task Transient_errors_end_in_failure_after_max_attempts()
    {
        var settings = Approval with { MaxAttempts = 2 };
        await PlanAsync(settings);
        var disable = (await OperationsAsync()).Single(o => o.Kind == ProvisioningKind.DisableAccount && o.AccountId == _rahimovaAcc);
        await ActAsync(ProvisioningAction.Approve, [disable.Id]);
        _directory.Fail = name => name == "disable" ? new ConnectorException("tarmoq", transient: true) : null;

        await ExecuteAsync(settings);
        _time.Now = _time.Now.AddMinutes(2);
        await ExecuteAsync(settings);

        var op = (await OperationsAsync()).Single(o => o.Id == disable.Id);
        Assert.Equal((ProvisioningStatus.Failed, 2), (op.Status, op.Attempts));
        Assert.Equal(1, (await ActAsync(ProvisioningAction.Dismiss, [op.Id])).Changed);
        Assert.Equal(ProvisioningStatus.Cancelled, (await OperationsAsync()).Single(o => o.Id == op.Id).Status);
    }

    [Fact]
    public async Task Joiner_gets_a_disabled_account_with_a_free_login_and_then_the_role_groups()
    {
        // "j.aliyev" boshqa xodimda band
        _directory.Accounts["other"] = new("other", "j.aliyev", "X-99", true);
        var settings = Approval with { CreateAccounts = true };

        await PlanAsync(settings);
        var create = (await OperationsAsync()).Single(o => o.Kind == ProvisioningKind.CreateAccount);
        Assert.Equal((_aliyev, "j.aliyev", "joiner"), (create.IdentityId!.Value, create.Summary, create.Reason));
        await ActAsync(ProvisioningAction.Approve);
        await ExecuteAsync(settings);

        Assert.Contains("create j.aliyev2", _directory.Calls);
        await using (var db = pg.CreateContext())
        {
            var account = await db.Accounts.SingleAsync(a => a.IdentityId == _aliyev, Ct);
            Assert.Equal(("j.aliyev2", AccountStatus.Disabled, "T4"), (account.Login, account.Status, account.EmployeeNumber));
            Assert.Equal(account.Id, (await db.ProvisioningOperations.SingleAsync(o => o.Id == create.Id, Ct)).AccountId);
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "provisioning.account-created" && e.TargetId == account.Id.ToString(), Ct));
        }

        // Keyingi rejada — rol guruhi yangi akkauntga
        await PlanAsync(settings);
        var add = (await OperationsAsync()).Single(o => o.Status == ProvisioningStatus.Proposed);
        Assert.Equal((ProvisioningKind.AddMembership, _credit), (add.Kind, add.EntitlementId));
    }

    [Fact]
    public async Task Account_created_by_a_lost_earlier_attempt_is_adopted_not_duplicated()
    {
        _directory.Accounts["prev"] = new("prev", "j.aliyev", "T4", false);
        var settings = Approval with { CreateAccounts = true };
        await PlanAsync(settings);
        await ActAsync(ProvisioningAction.Approve);

        await ExecuteAsync(settings);

        Assert.DoesNotContain(_directory.Calls, c => c.StartsWith("create"));
        await using var db = pg.CreateContext();
        Assert.Equal("prev", (await db.Accounts.SingleAsync(a => a.IdentityId == _aliyev, Ct)).ExternalId);
    }

    [Fact]
    public async Task Nothing_is_planned_while_the_last_sync_failed()
    {
        await using (var db = pg.CreateContext())
        {
            db.ConnectorRuns.Add(new ConnectorRun { ApplicationId = _app, TriggeredBy = "system", Status = ConnectorRunStatus.Aborted, StartedAt = _time.Now });
            await db.SaveChangesAsync(Ct);
        }

        var plan = await PlanAsync();

        Assert.Contains("eskirgan", plan.Skipped);
        Assert.Empty(await OperationsAsync());
    }

    [Fact]
    public async Task Two_workers_never_execute_the_same_operation_twice()
    {
        await PlanAsync();
        await ActAsync(ProvisioningAction.Approve);

        var results = await Task.WhenAll(ExecuteAsync(), ExecuteAsync(), ExecuteAsync());

        Assert.Equal(5, results.Sum(r => r.Succeeded));
        Assert.Equal(5, _directory.Calls.Count);
    }

    [Fact]
    public async Task Approving_is_refused_for_a_system_with_writing_disabled_and_only_proposed_operations_are_approved()
    {
        await PlanAsync();
        await using (var db = pg.CreateContext())
        {
            var none = await new ProvisioningService(db, new AuditLog(db, _time), _time)
                .ApplyAsync(ProvisioningAction.Approve, await db.ProvisioningOperations.Select(o => o.Id).ToListAsync(Ct), new HashSet<Guid>(), "boss", null, ct: Ct);
            Assert.Equal((0, 5), (none.Changed, none.Skipped));
        }
        Assert.Equal(5, (await ActAsync(ProvisioningAction.Approve)).Changed);
        Assert.Equal((0, 5), ((await ActAsync(ProvisioningAction.Approve)).Changed, (await ActAsync(ProvisioningAction.Approve)).Skipped));
    }

    [Fact]
    public async Task Approved_request_becomes_fulfilled_once_the_group_is_really_there()
    {
        Guid requestId;
        await using (var db = pg.CreateContext())
        {
            var request = new AccessRequest
            {
                RequesterId = _karimov, BeneficiaryId = _karimov, RoleId = _kreditRole, Justification = "Kredit ishlari uchun",
                Status = AccessRequestStatus.Approved, ClosedAt = _time.Now
            };
            db.AccessRequests.Add(request);
            await db.SaveChangesAsync(Ct);
            requestId = request.Id;
            Assert.Equal(0, await new ProvisioningPlanner(db, new AuditLog(db, _time), _time).CompleteRequestsAsync("system", null, Ct));
        }
        await PlanAsync();
        await ActAsync(ProvisioningAction.Approve);
        await ExecuteAsync();

        await using (var db = pg.CreateContext())
        {
            Assert.Equal(1, await new ProvisioningPlanner(db, new AuditLog(db, _time), _time).CompleteRequestsAsync("system", null, Ct));
            Assert.Equal(AccessRequestStatus.Fulfilled, (await db.AccessRequests.SingleAsync(r => r.Id == requestId, Ct)).Status);
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "request.fulfilled" && e.TargetId == requestId.ToString(), Ct));
        }
    }

    [Fact]
    public async Task Interrupted_operations_return_to_the_queue_on_startup()
    {
        await PlanAsync();
        await using var db = pg.CreateContext();
        await db.ProvisioningOperations.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, ProvisioningStatus.Running), Ct);

        Assert.Equal(5, await ProvisioningService.RecoverInterruptedAsync(db, _time, Ct));
        Assert.All(await OperationsAsync(), o => Assert.Equal(ProvisioningStatus.Queued, o.Status));
    }

    [Theory]
    [InlineData("Aziz", "Karimov", "a.karimov")]
    [InlineData("O'tkir", "G'ulomov", "o.gulomov")]
    [InlineData("Шоҳрух", "Тошматов", "s.toshmatov")]
    [InlineData("Élodie", "Müller-Øst", "e.mullerst")]
    [InlineData("Abdulaziz", "Abdurahmonovaxonovich", "a.abdurahmonovaxonov")]
    public void Login_is_ascii_first_initial_dot_last_name_cut_to_the_limit(string first, string last, string expected)
    {
        var candidates = LoginGenerator.Candidates(first, last, 20).Take(3).ToList();

        Assert.Equal(expected, candidates[0]);
        Assert.True(candidates.All(c => c.Length <= 20));
        Assert.EndsWith("2", candidates[1]);
        Assert.Matches("^[a-z0-9.]+$", candidates[2]);
    }

    [Fact]
    public void No_login_from_names_without_latin_letters()
    {
        Assert.Empty(LoginGenerator.Candidates("—", "Karimov", 20));
    }
}
