using Bank.Idm.Infrastructure.Requests;
using Bank.Idm.Infrastructure.Reviews;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Tests;

/// <summary>Avtomatik rollar, "bo'lishi kerak / haqiqatda bor" farqi va SoD. Har bir test o'z bazasida.</summary>
public class AccessTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private Application _ad = null!;
    private Entitlement _credit = null!, _approve = null!, _admins = null!;
    private Identity _karimov = null!, _rahimova = null!, _aliyev = null!;

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using var db = pg.CreateContext();
        _ad = new Application { Code = "AD", Name = "AD", Kind = ApplicationKind.Directory };
        _credit = new Entitlement { Application = _ad, ExternalId = "g1", Name = "kredit-berish" };
        _approve = new Entitlement { Application = _ad, ExternalId = "g2", Name = "kredit-tasdiqlash" };
        _admins = new Entitlement { Application = _ad, ExternalId = "g3", Name = "Domain Admins", IsPrivileged = true };
        _karimov = new Identity { EmployeeNumber = "T1", FirstName = "Aziz", LastName = "Karimov", Department = "Kredit bo'limi", JobTitle = "Mutaxassis" };
        _rahimova = new Identity { EmployeeNumber = "T2", FirstName = "Dilnoza", LastName = "Rahimova", Department = "kredit BO'LIMI ", JobTitle = "Boshliq" };
        _aliyev = new Identity { EmployeeNumber = "T3", FirstName = "Jasur", LastName = "Aliyev", Department = "IT" };
        db.AddRange(_ad, _credit, _approve, _admins, _karimov, _rahimova, _aliyev);
        await db.SaveChangesAsync(Ct);
    }

    public ValueTask DisposeAsync() => pg.DisposeAsync();

    private async Task<Role> RoleAsync(string code, string? department = null, string? title = null, params Entitlement[] entitlements)
    {
        await using var db = pg.CreateContext();
        var role = new Role { Code = code, Name = code };
        role.Entitlements.AddRange(entitlements.Select(e => new RoleEntitlement { EntitlementId = e.Id }));
        if (department is not null || title is not null) role.Rules.Add(new RoleRule { Department = department, JobTitle = title, CreatedBy = "test" });
        db.Roles.Add(role);
        await db.SaveChangesAsync(Ct);
        return role;
    }

    private async Task<BirthrightResult> RecalculateAsync()
    {
        await using var db = pg.CreateContext();
        return await new BirthrightService(db, new AuditLog(db, TimeProvider.System), TimeProvider.System).RecalculateAsync("boss", "Boss", Ct);
    }

    private async Task<List<string>> ActiveRolesAsync(Identity i)
    {
        await using var db = pg.CreateContext();
        return await db.IdentityRoles.Where(r => r.IdentityId == i.Id && r.ValidTo == null).Select(r => r.Role.Code).OrderBy(c => c).ToListAsync(Ct);
    }

    private async Task GiveAsync(Identity i, params Entitlement[] entitlements)
    {
        await using var db = pg.CreateContext();
        var account = new Account { ApplicationId = _ad.Id, IdentityId = i.Id, ExternalId = $"u-{i.EmployeeNumber}", Login = i.EmployeeNumber };
        account.Entitlements.AddRange(entitlements.Select(e => new AccountEntitlement { EntitlementId = e.Id }));
        db.Accounts.Add(account);
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Birthright_roles_follow_department_and_title_and_are_idempotent()
    {
        await RoleAsync("KREDIT", "Kredit bo'limi", null, _credit);
        await RoleAsync("KREDIT-BOSHLIQ", "Kredit bo'limi", "Boshliq", _approve);

        var first = await RecalculateAsync();
        Assert.Equal((3, 0), (first.Assigned, first.Removed));
        Assert.Equal(["KREDIT"], await ActiveRolesAsync(_karimov));
        Assert.Equal(["KREDIT", "KREDIT-BOSHLIQ"], await ActiveRolesAsync(_rahimova));   // katta-kichik harf va bo'shliq farqsiz
        Assert.Empty(await ActiveRolesAsync(_aliyev));

        var again = await RecalculateAsync();
        Assert.Equal((0, 0), (again.Assigned, again.Removed));
    }

    [Fact]
    public async Task Moving_or_leaving_removes_birthright_roles_but_keeps_manual_ones_and_history()
    {
        var kredit = await RoleAsync("KREDIT", "Kredit bo'limi", null, _credit);
        var vip = await RoleAsync("VIP");
        await RecalculateAsync();
        await using (var db = pg.CreateContext())
        {
            db.IdentityRoles.Add(new IdentityRole { IdentityId = _karimov.Id, RoleId = vip.Id, Source = RoleAssignmentSource.Manual });
            (await db.Identities.SingleAsync(i => i.Id == _karimov.Id, Ct)).Department = "Riskler";
            (await db.Identities.SingleAsync(i => i.Id == _rahimova.Id, Ct)).Status = IdentityStatus.Terminated;
            await db.SaveChangesAsync(Ct);
        }

        var result = await RecalculateAsync();

        Assert.Equal(2, result.Removed);
        Assert.Equal(["VIP"], await ActiveRolesAsync(_karimov));
        Assert.Empty(await ActiveRolesAsync(_rahimova));
        await using var check = pg.CreateContext();
        Assert.Equal(2, await check.IdentityRoles.CountAsync(r => r.RoleId == kredit.Id && r.ValidTo != null, Ct));   // tarix o'chmaydi
        Assert.Equal(2, await check.AuditEvents.CountAsync(e => e.Action == "identity.role-removed", Ct));
    }

    [Fact]
    public async Task Analyzer_shows_granted_missing_and_excess_access()
    {
        await RoleAsync("KREDIT", "Kredit bo'limi", null, _credit, _approve);
        await RecalculateAsync();
        await GiveAsync(_karimov, _credit, _admins);

        await using var db = pg.CreateContext();
        var lines = await new AccessAnalyzer(db, TimeProvider.System).ForIdentityAsync(_karimov.Id, Ct);

        Assert.Equal(AccessState.Granted, lines.Single(l => l.EntitlementName == "kredit-berish").State);
        Assert.Equal(AccessState.Missing, lines.Single(l => l.EntitlementName == "kredit-tasdiqlash").State);
        var excess = lines.Single(l => l.EntitlementName == "Domain Admins");
        Assert.Equal((AccessState.Excess, true), (excess.State, excess.IsPrivileged));
        var drift = await new AccessAnalyzer(db, TimeProvider.System).DriftAsync(Ct);
        Assert.Equal((1, 1), drift.Where(d => d.IdentityId == _karimov.Id).Select(d => (d.Missing, d.Excess)).Single());
    }

    [Fact]
    public async Task Sod_violation_is_found_on_actual_access_and_on_role_definition()
    {
        await using (var db = pg.CreateContext())
        {
            var rule = new SodRule { Code = "KREDIT-SOD", Name = "Kredit berish va tasdiqlash", CreatedBy = "test" };
            rule.Entitlements.Add(new SodRuleEntitlement { EntitlementId = _credit.Id, Side = SodSide.Left });
            rule.Entitlements.Add(new SodRuleEntitlement { EntitlementId = _approve.Id, Side = SodSide.Right });
            db.SodRules.Add(rule);
            await db.SaveChangesAsync(Ct);
        }
        await GiveAsync(_karimov, _credit, _approve);
        await GiveAsync(_rahimova, _approve);

        await using var check = pg.CreateContext();
        var analyzer = new AccessAnalyzer(check, TimeProvider.System);
        var violation = Assert.Single(await analyzer.SodViolationsAsync(null, Ct));
        Assert.Equal((_karimov.Id, "KREDIT-SOD"), (violation.IdentityId, violation.RuleCode));
        Assert.Equal(["KREDIT-SOD"], await analyzer.RoleConflictsAsync([_credit.Id, _approve.Id], Ct));
        Assert.Empty(await analyzer.RoleConflictsAsync([_credit.Id, _admins.Id], Ct));
    }

    [Fact]
    public async Task Sod_exception_marks_the_violation_accepted_until_it_expires_or_is_revoked()
    {
        Guid ruleId;
        await using (var db = pg.CreateContext())
        {
            var rule = new SodRule { Code = "KREDIT-SOD", Name = "Kredit berish va tasdiqlash", CreatedBy = "test" };
            rule.Entitlements.Add(new SodRuleEntitlement { EntitlementId = _credit.Id, Side = SodSide.Left });
            rule.Entitlements.Add(new SodRuleEntitlement { EntitlementId = _approve.Id, Side = SodSide.Right });
            db.SodRules.Add(rule);
            await db.SaveChangesAsync(Ct);
            ruleId = rule.Id;
        }
        await GiveAsync(_karimov, _credit, _approve);
        // 2026-11-03 12:00 Toshkent
        var time = new MutableTime(new DateTimeOffset(2026, 11, 3, 7, 0, 0, TimeSpan.Zero));
        var admin = new RequestActor("boss", "Boss", _rahimova.Id, IsAdmin: true);
        var today = new DateOnly(2026, 11, 3);
        NewSodException Input(DateOnly until, string reason = "Filialda ikkinchi mutaxassis yo'q", Guid? who = null) =>
            new(ruleId, who ?? _karimov.Id, reason, "Har kuni bo'lim boshlig'i barcha kreditlarni tekshiradi", until);
        async Task<ReviewResult<SodException>> CreateAsync(RequestActor actor, NewSodException input)
        {
            await using var db = pg.CreateContext();
            return await new SodExceptionService(db, new AuditLog(db, time), time).CreateAsync(actor, input, Ct);
        }
        async Task<SodViolation> ViolationAsync()
        {
            await using var db = pg.CreateContext();
            return Assert.Single(await new AccessAnalyzer(db, time).SodViolationsAsync(null, Ct));
        }

        // Tekshiruvlar: faqat admin, asos, muddat, o'ziga emas
        Assert.Equal(403, (await CreateAsync(admin with { IsAdmin = false }, Input(today.AddDays(30)))).Status);
        Assert.Equal(400, (await CreateAsync(admin, Input(today.AddDays(30), reason: "qisqa"))).Status);
        Assert.Equal(400, (await CreateAsync(admin, Input(today))).Status);
        Assert.Equal(400, (await CreateAsync(admin, Input(today.AddDays(367)))).Status);
        Assert.Equal(403, (await CreateAsync(admin with { IdentityId = _karimov.Id }, Input(today.AddDays(30)))).Status);
        Assert.Null((await ViolationAsync()).Exception);

        var granted = await CreateAsync(admin, Input(today.AddDays(30)));
        Assert.NotNull(granted.Value);
        Assert.Equal(409, (await CreateAsync(admin, Input(today.AddDays(60)))).Status);
        var accepted = await ViolationAsync();
        Assert.Equal((granted.Value.Id, "Boss"), (accepted.Exception!.Id, accepted.Exception.ApprovedBy));
        // Oxirgi kun to'liq kiradi: 2026-12-03 23:59 (Toshkent) gacha
        Assert.Equal(new DateTimeOffset(2026, 12, 3, 19, 0, 0, TimeSpan.Zero), granted.Value.ValidTo);

        // Muddat tugadi — buzilish yana ochiq
        time.Now = granted.Value.ValidTo;
        Assert.Null((await ViolationAsync()).Exception);

        // Yangi istisno va bekor qilish
        time.Now = new DateTimeOffset(2026, 12, 10, 7, 0, 0, TimeSpan.Zero);
        var second = await CreateAsync(admin, Input(new DateOnly(2027, 1, 10)));
        Assert.NotNull((await ViolationAsync()).Exception);
        await using (var db = pg.CreateContext())
        {
            var service = new SodExceptionService(db, new AuditLog(db, time), time);
            Assert.NotNull((await service.RevokeAsync(admin, second.Value!.Id, "Ikkinchi mutaxassis ishga olindi", Ct)).Value);
            Assert.Equal(409, (await service.RevokeAsync(admin, second.Value.Id, null, Ct)).Status);
        }
        Assert.Null((await ViolationAsync()).Exception);
        await using (var db = pg.CreateContext())
        {
            Assert.Equal(2, await db.AuditEvents.CountAsync(e => e.Action == "sod.exception-granted", Ct));
            Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.Action == "sod.exception-revoked", Ct));
        }
    }
}
