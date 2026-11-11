using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Access;

public enum AccessState
{
    /// <summary>Rol bo'yicha bo'lishi kerak va tizimda bor.</summary>
    Granted,
    /// <summary>Rol bo'yicha bo'lishi kerak, lekin tizimda yo'q (G bosqichida beriladi).</summary>
    Missing,
    /// <summary>Tizimda bor, lekin hech qaysi rol bilan asoslanmagan (attestatsiyada ko'rib chiqiladi).</summary>
    Excess
}

public record AccessLine(Guid EntitlementId, string ApplicationCode, string EntitlementName, bool IsPrivileged, AccessState State,
    IReadOnlyList<string> ViaRoles, IReadOnlyList<string> Accounts)
{
    /// <summary>Faqat ichma-ich guruh orqali bor bo'lsa — qaysi bevosita guruh(lar) orqali; bevosita a'zolik bo'lsa bo'sh.</summary>
    public IReadOnlyList<string> InheritedVia { get; init; } = [];
}

public record DriftSummary(Guid IdentityId, int Missing, int Excess);

/// <param name="Excess">Asossiz ruxsatlar soni; <paramref name="Privileged"/> — ulardan imtiyozlilari; <paramref name="Identities"/> — nechta xodimda.</param>
public record RiskCell(string Department, string Application, int Excess, int Privileged, int Identities);

public record SodViolation(Guid RuleId, string RuleCode, string RuleName, RiskLevel Risk, Guid IdentityId,
    IReadOnlyList<string> LeftEntitlements, IReadOnlyList<string> RightEntitlements)
{
    /// <summary>Amaldagi istisno (bo'lsa): buzilish ma'lum va kompensatsion nazorat ostida, ochiq buzilishlar soniga kirmaydi.</summary>
    public SodExceptionInfo? Exception { get; init; }
}

public record SodExceptionInfo(Guid Id, DateTimeOffset ValidTo, string CompensatingControl, string ApprovedBy);

/// <summary>"Bo'lishi kerak" (rollar) va "haqiqatda bor" (tizimlardan o'qilgan) ruxsatlarni solishtiradi, SoD buzilishlarini topadi.</summary>
public class AccessAnalyzer(IdmDbContext db, TimeProvider time)
{
    // init xususiyatlar (konstruktor emas): EF Core ularning ustidan Where/Select'ni SQL'ga tarjima qila oladi
    private class Expected { public Guid IdentityId { get; init; } public Guid EntitlementId { get; init; } public string RoleCode { get; init; } = null!; }
    private class Actual
    {
        public Guid IdentityId { get; init; } public Guid EntitlementId { get; init; } public string Login { get; init; } = null!;
        public string? Via { get; init; }
    }

    private IQueryable<Expected> ExpectedQuery()
    {
        var now = time.GetUtcNow();
        return from ir in db.IdentityRoles
               where ir.ValidFrom <= now && (ir.ValidTo == null || ir.ValidTo > now)
               join re in db.RoleEntitlements on ir.RoleId equals re.RoleId
               where re.Entitlement.DeletedAt == null
               select new Expected { IdentityId = ir.IdentityId, EntitlementId = re.EntitlementId, RoleCode = ir.Role.Code };
    }

    private IQueryable<Actual> ActualQuery() =>
        from ae in db.AccountEntitlements
        where ae.Account.IdentityId != null && ae.Account.Status != AccountStatus.Deleted
        select new Actual
        {
            IdentityId = ae.Account.IdentityId!.Value, EntitlementId = ae.EntitlementId, Login = ae.Account.Login,
            Via = db.Entitlements.Where(v => v.Id == ae.ViaEntitlementId).Select(v => v.Name).FirstOrDefault()
        };

    public async Task<IReadOnlyList<AccessLine>> ForIdentityAsync(Guid identityId, CancellationToken ct = default)
    {
        var expected = await ExpectedQuery().Where(e => e.IdentityId == identityId).ToListAsync(ct);
        var actual = await ActualQuery().Where(a => a.IdentityId == identityId).ToListAsync(ct);
        var ids = expected.Select(e => e.EntitlementId).Concat(actual.Select(a => a.EntitlementId)).Distinct().ToList();
        var entitlements = await db.Entitlements.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.IsPrivileged, Code = e.Application.Code }).ToDictionaryAsync(e => e.Id, ct);
        return ids.Select(id =>
            {
                var via = expected.Where(e => e.EntitlementId == id).Select(e => e.RoleCode).Distinct().Order().ToList();
                var held = actual.Where(a => a.EntitlementId == id).ToList();
                var accounts = held.Select(a => a.Login).Distinct().Order().ToList();
                var inherited = held.All(a => a.Via is not null) ? held.Select(a => a.Via!).Distinct().Order().ToList() : [];
                var state = via.Count > 0 ? accounts.Count > 0 ? AccessState.Granted : AccessState.Missing : AccessState.Excess;
                var e = entitlements[id];
                return new AccessLine(id, e.Code, e.Name, e.IsPrivileged, state, via, accounts) { InheritedVia = inherited };
            })
            .OrderBy(l => l.State == AccessState.Granted).ThenByDescending(l => l.IsPrivileged).ThenBy(l => l.ApplicationCode).ThenBy(l => l.EntitlementName)
            .ToList();
    }

    /// <summary>Har bir xodim uchun yetishmayotgan va ortiqcha ruxsatlar soni (faqat farqi bor xodimlar).</summary>
    public async Task<IReadOnlyList<DriftSummary>> DriftAsync(CancellationToken ct = default)
    {
        var expected = (await ExpectedQuery().Select(e => new { e.IdentityId, e.EntitlementId }).Distinct().ToListAsync(ct))
            .Select(e => (e.IdentityId, e.EntitlementId)).ToHashSet();
        var actual = (await ActualQuery().Select(a => new { a.IdentityId, a.EntitlementId }).Distinct().ToListAsync(ct))
            .Select(a => (a.IdentityId, a.EntitlementId)).ToHashSet();
        var missing = expected.Except(actual).GroupBy(x => x.IdentityId).ToDictionary(g => g.Key, g => g.Count());
        var excess = actual.Except(expected).GroupBy(x => x.IdentityId).ToDictionary(g => g.Key, g => g.Count());
        return missing.Keys.Union(excess.Keys)
            .Select(id => new DriftSummary(id, missing.GetValueOrDefault(id), excess.GetValueOrDefault(id))).ToList();
    }

    /// <summary>
    /// Xavf xaritasi: bo'lim × tizim bo'yicha asossiz ruxsatlar (tizimda bor, hech qaysi rol bilan asoslanmagan; ishdan ketmagan xodimlar)
    /// va ulardan imtiyozlilari. Bo'limsiz xodimlar — bo'sh satr.
    /// </summary>
    public async Task<IReadOnlyList<RiskCell>> RiskMatrixAsync(CancellationToken ct = default)
    {
        var expected = (await ExpectedQuery().Select(e => new { e.IdentityId, e.EntitlementId }).Distinct().ToListAsync(ct))
            .Select(e => (e.IdentityId, e.EntitlementId)).ToHashSet();
        var actual = await (
            from ae in db.AccountEntitlements
            where ae.Account.IdentityId != null && ae.Account.Status != AccountStatus.Deleted && ae.Entitlement.DeletedAt == null
                && ae.Account.Identity!.Status != IdentityStatus.Terminated
            select new { IdentityId = ae.Account.IdentityId!.Value, ae.EntitlementId, ae.Account.Identity!.Department, App = ae.Entitlement.Application.Code,
                ae.Entitlement.IsPrivileged }).Distinct().ToListAsync(ct);
        return actual.Where(a => !expected.Contains((a.IdentityId, a.EntitlementId)))
            .GroupBy(a => (Department: a.Department?.Trim() ?? "", a.App))
            .Select(g => new RiskCell(g.Key.Department, g.Key.App, g.Count(), g.Count(x => x.IsPrivileged), g.Select(x => x.IdentityId).Distinct().Count()))
            .ToList();
    }

    /// <summary>Haqiqiy ruxsatlar bo'yicha SoD buzilishlari: xodimda qoidaning ikkala tomonidan kamida bittadan ruxsat bor.</summary>
    public async Task<IReadOnlyList<SodViolation>> SodViolationsAsync(Guid? identityId = null, CancellationToken ct = default)
    {
        var rules = await db.SodRules.AsNoTracking().Include(r => r.Entitlements).ThenInclude(e => e.Entitlement).ToListAsync(ct);
        if (rules.Count == 0) return [];
        var watched = rules.SelectMany(r => r.Entitlements.Select(e => e.EntitlementId)).Distinct().ToList();
        var query = ActualQuery().Where(a => watched.Contains(a.EntitlementId));
        if (identityId is not null) query = query.Where(a => a.IdentityId == identityId);
        var held = (await query.Select(a => new { a.IdentityId, a.EntitlementId }).Distinct().ToListAsync(ct)).ToLookup(a => a.IdentityId, a => a.EntitlementId);

        var now = time.GetUtcNow();
        var exceptionQuery = db.SodExceptions.AsNoTracking().Where(x => x.RevokedAt == null && x.ValidFrom <= now && x.ValidTo > now);
        if (identityId is not null) exceptionQuery = exceptionQuery.Where(x => x.IdentityId == identityId);
        var exceptions = (await exceptionQuery.ToListAsync(ct)).GroupBy(x => (x.SodRuleId, x.IdentityId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ValidTo).First());

        var result = new List<SodViolation>();
        foreach (var rule in rules)
        {
            var left = rule.Entitlements.Where(e => e.Side == SodSide.Left).ToDictionary(e => e.EntitlementId, e => e.Entitlement.Name);
            var right = rule.Entitlements.Where(e => e.Side == SodSide.Right).ToDictionary(e => e.EntitlementId, e => e.Entitlement.Name);
            foreach (var group in held)
            {
                var l = group.Where(left.ContainsKey).Select(id => left[id]).Order().ToList();
                var r = group.Where(right.ContainsKey).Select(id => right[id]).Order().ToList();
                if (l.Count == 0 || r.Count == 0) continue;
                var x = exceptions.GetValueOrDefault((rule.Id, group.Key));
                result.Add(new SodViolation(rule.Id, rule.Code, rule.Name, rule.Risk, group.Key, l, r)
                {
                    Exception = x is null ? null : new SodExceptionInfo(x.Id, x.ValidTo, x.CompensatingControl, x.CreatedByName ?? x.CreatedBy)
                });
            }
        }
        return result.OrderBy(v => v.Exception is not null).ThenByDescending(v => v.Risk).ThenBy(v => v.RuleCode).ToList();
    }

    /// <summary>
    /// Xodimga yangi ruxsatlar (rol) berilsa paydo bo'ladigan SoD buzilishlari: hozirgi (haqiqiy + rollar bo'yicha) ruxsatlar bilan
    /// birga tekshiriladi va faqat yangi ruxsat qatnashgan ziddiyatlar qaytadi (eski buzilish arizaga yuklanmaydi).
    /// </summary>
    public async Task<IReadOnlyList<SodViolation>> SodPreviewAsync(Guid identityId, IReadOnlyCollection<Guid> added, CancellationToken ct = default)
    {
        var rules = await db.SodRules.AsNoTracking().Include(r => r.Entitlements).ThenInclude(e => e.Entitlement).ToListAsync(ct);
        if (rules.Count == 0 || added.Count == 0) return [];
        var held = (await ActualQuery().Where(a => a.IdentityId == identityId).Select(a => a.EntitlementId).ToListAsync(ct))
            .Concat(await ExpectedQuery().Where(e => e.IdentityId == identityId).Select(e => e.EntitlementId).ToListAsync(ct))
            .ToHashSet();
        var after = held.Concat(added).ToHashSet();
        var result = new List<SodViolation>();
        foreach (var rule in rules)
        {
            var left = rule.Entitlements.Where(e => e.Side == SodSide.Left && after.Contains(e.EntitlementId)).ToList();
            var right = rule.Entitlements.Where(e => e.Side == SodSide.Right && after.Contains(e.EntitlementId)).ToList();
            if (left.Count == 0 || right.Count == 0) continue;
            if (!left.Concat(right).Any(e => added.Contains(e.EntitlementId))) continue;
            result.Add(new SodViolation(rule.Id, rule.Code, rule.Name, rule.Risk, identityId,
                left.Select(e => e.Entitlement.Name).Order().ToList(), right.Select(e => e.Entitlement.Name).Order().ToList()));
        }
        return result;
    }

    /// <summary>Rolning o'zi SoD qoidasini buzadimi (ikkala tomondan ruxsat bitta rolda): rol yaratishda ogohlantirish uchun.</summary>
    public async Task<IReadOnlyList<string>> RoleConflictsAsync(IReadOnlyCollection<Guid> entitlementIds, CancellationToken ct = default)
    {
        var rules = await db.SodRules.AsNoTracking().Include(r => r.Entitlements).ToListAsync(ct);
        return rules.Where(r => r.Entitlements.Any(e => e.Side == SodSide.Left && entitlementIds.Contains(e.EntitlementId))
                && r.Entitlements.Any(e => e.Side == SodSide.Right && entitlementIds.Contains(e.EntitlementId)))
            .Select(r => r.Code).ToList();
    }
}
