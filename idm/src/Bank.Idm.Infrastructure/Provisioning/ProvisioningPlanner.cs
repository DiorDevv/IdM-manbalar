using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Connectors;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Provisioning;

/// <param name="UpdateAccounts">HR manbali xodim akkauntini yangilash: bo'lim, lavozim, filial, rahbar va OU (o'tish, ishdan ketish).</param>
/// <param name="BranchContainers">Filial → OU. Filiali xaritada yo'q faol xodim akkaunti joyidan qo'zg'atilmaydi.</param>
/// <param name="DefaultContainer">Qayta ishga olingan xodim (akkaunti ishdan ketganlar OU'sida), filiali xaritada bo'lmasa — shu yerga.</param>
/// <param name="LeaversContainer">Ishdan ketganlar OU'si (bo'sh — ko'chirilmaydi).</param>
/// <param name="ManageBranch">Tizimda filial atributi bor (yoziladi).</param>
public record ProvisioningSettings(ProvisioningMode Mode, bool CreateAccounts = false, int MaxAutoOperations = 20,
    IReadOnlyCollection<string>? ProtectedLogins = null, bool DisableSuspended = true, int MaxAttempts = 5, int LoginMaxLength = 20,
    bool EnableReturning = true, bool EnableNewAccounts = false, bool UpdateAccounts = false,
    IReadOnlyDictionary<string, string>? BranchContainers = null, string? DefaultContainer = null, string? LeaversContainer = null, bool ManageBranch = false);

/// <summary>Akkauntning kerakli joyi va atributlari (rejalashtiruvchi va bajaruvchi uchun bitta qoida).</summary>
public static class AccountTargets
{
    /// <summary>Kerakli OU yoki null (joyidan qo'zg'atilmaydi).</summary>
    public static string? Container(ProvisioningSettings s, IdentityStatus status, string? branch, string? currentDn)
    {
        if (status == IdentityStatus.Terminated) return s.LeaversContainer;
        if (branch is not null && s.BranchContainers is { } map && map.TryGetValue(branch.Trim(), out var ou)) return ou;
        // Qayta ishga olingan: ishdan ketganlar OU'sidan standart joyga
        return s.LeaversContainer is { } leavers && currentDn is not null && ParentIs(currentDn, leavers) ? s.DefaultContainer : null;
    }

    public static AccountAttributes Attributes(ProvisioningSettings s, string? department, string? jobTitle, string? branch, string? managerDn) =>
        new(Clean(department), Clean(jobTitle), s.ManageBranch ? Clean(branch) : null, managerDn);

    /// <summary>Hozirgi holatdan farqi (ko'rsatish uchun); farq yo'q bo'lsa — bo'sh ro'yxat.</summary>
    public record State(string? Dn, string? Department, string? JobTitle, string? Branch, string? ManagerDn);

    public static List<string> Differences(ProvisioningSettings s, AccountAttributes? want, string? container, State a)
    {
        var diff = new List<string>();
        void Check(string name, string? from, string? to, bool dn = false)
        {
            if (!string.Equals(Clean(from), Clean(to), dn ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                diff.Add($"{name}: {Clean(from) ?? "—"} → {Clean(to) ?? "—"}");
        }
        if (want is not null)
        {
            Check("bo'lim", a.Department, want.Department);
            Check("lavozim", a.JobTitle, want.JobTitle);
            if (s.ManageBranch) Check("filial", a.Branch, want.Branch);
            if (want.ManagerDn is not null) Check("rahbar", a.ManagerDn, want.ManagerDn, dn: true);
        }
        if (container is not null && a.Dn is { } current && !ParentIs(current, container))
            diff.Add($"OU: {Parent(current)} → {container}");
        return diff;
    }

    public static bool ParentIs(string dn, string container) => Normalize(Parent(dn)) == Normalize(container);

    private static string Parent(string dn) => Split(dn).Parent;
    public static string Rdn(string dn) => Split(dn).Rdn;

    /// <summary>DN → (birinchi komponent, ota-ona); ekranlangan vergul ("\\,") bo'lish emas.</summary>
    private static (string Rdn, string Parent) Split(string dn)
    {
        for (var i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\') { i++; continue; }
            if (dn[i] == ',') return (dn[..i], dn[(i + 1)..].TrimStart());
        }
        return (dn, "");
    }

    private static string Normalize(string dn) => string.Join(',', dn.Split(',').Select(p => p.Trim())).ToLowerInvariant();
    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <param name="Skipped">Reja tuzilmadi: sababi (masalan oxirgi sinxronlash muvaffaqiyatsiz).</param>
/// <param name="Held">Avtomatik rejimda amallar soni chegaradan oshdi: yangi amallar tasdiqqa to'xtatildi.</param>
public record PlanResult(int Planned, int Created, int Obsoleted, bool Held, string? Skipped, IReadOnlyList<string> Warnings);

/// <summary>
/// "Bo'lishi kerak" (rollar, xodim holati) va tizimdan o'qilgan holatni solishtirib, tizimga yozish amallarini rejalashtiradi:
/// <list type="bullet">
/// <item>ishdan ketgan xodim — barcha akkauntlari bloklanadi va guruhlardan chiqariladi;</item>
/// <item>vaqtincha to'xtatilgan — bloklanadi (sozlamaga ko'ra);</item>
/// <item>rol bo'yicha kerak, lekin yo'q guruh — shaxsiy akkauntga qo'shiladi; akkaunt yo'q bo'lsa (sozlamaga ko'ra) yaratiladi;</item>
/// <item>rol olib tashlangan — faqat IdM o'zi qo'shgan a'zolik olib tashlanadi. Qo'lda berilgan ortiqcha ruxsatlarga tegilmaydi
/// (ularni attestatsiyada rahbar ko'rib chiqadi): noto'g'ri sozlangan rol butun bankning ruxsatlarini o'chirib yubormasligi uchun;</item>
/// <item>attestatsiyada rahbar olib tashlagan ortiqcha guruh — olib tashlanadi (rol bo'yicha kerak bo'lib qolmagan bo'lsa);</item>
/// <item>qayta ishga olingan yoki to'xtatilishi tugagan xodim — IdM o'zi bloklagan akkaunti qayta yoqiladi; IdM yaratgan akkaunt — ishga
/// kirish kuni (sozlamaga ko'ra). Akkauntning oxirgi holatini IdM emas, boshqa kimdir o'zgartirgan bo'lsa (administrator qo'lda
/// bloklagan) — tegilmaydi.</item>
/// </list>
/// Xizmat akkauntlari va himoyalangan loginlar hech qachon o'zgartirilmaydi. Reja faqat oxirgi sinxronlash muvaffaqiyatli bo'lsa tuziladi
/// (eskirgan ma'lumot bilan yozilmaydi). Keraksiz bo'lib qolgan kutilayotgan amallar bekor qilinadi (Obsolete).
/// </summary>
public class ProvisioningPlanner(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <param name="Entitlements">Haqiqatda bor (bevosita va meros) — "kerak guruh bormi" uchun.</param>
    /// <param name="Direct">Faqat bevosita a'zolik — olib tashlash uchun (meros a'zolikni ichki guruhdan chiqmasdan olib bo'lmaydi).</param>
    private record AccountRow(Guid Id, Guid? IdentityId, string Login, AccountStatus Status, AccountKind Kind, List<Guid> Entitlements, List<Guid> Direct,
        AccountTargets.State State, bool OrgSynced);
    private record Planned(ProvisioningKind Kind, Guid? IdentityId, Guid? AccountId, Guid? EntitlementId, string Reason, string? Summary);

    public static string DedupKey(Guid applicationId, ProvisioningKind kind, Guid? identityId, Guid? accountId, Guid? entitlementId) =>
        $"{applicationId:N}:{kind}:{(kind == ProvisioningKind.CreateAccount ? identityId : accountId):N}:{entitlementId:N}";

    public async Task<PlanResult> PlanAsync(Guid applicationId, ProvisioningSettings settings, string actorId, string? actorName, CancellationToken ct = default)
    {
        if (settings.Mode == ProvisioningMode.Off) return new PlanResult(0, 0, 0, false, "Tizimga yozish o'chirilgan", []);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ProvisioningLocks.AcquireAsync(db, applicationId, ct);
        var app = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId, ct);
        var now = time.GetUtcNow();

        // Sinxronlash ketayotgan bo'lsa (istalgan server nusxasida) — yarim yangilangan holat bilan reja tuzilmaydi
        if (await db.ConnectorRuns.AnyAsync(r => r.ApplicationId == app.Id && r.Status == ConnectorRunStatus.Running, ct))
            return new PlanResult(0, 0, 0, false, $"{app.Code} hozir sinxronlanmoqda, birozdan keyin qayta urining", []);
        var lastRun = await db.ConnectorRuns.Where(r => r.ApplicationId == app.Id && r.Status != ConnectorRunStatus.Running)
            .OrderByDescending(r => r.StartedAt).Select(r => (ConnectorRunStatus?)r.Status).FirstOrDefaultAsync(ct);
        if (lastRun != ConnectorRunStatus.Succeeded)
            return new PlanResult(0, 0, 0, false, lastRun is null
                ? $"{app.Code} hali sinxronlanmagan: tizim holati noma'lum"
                : $"{app.Code}: oxirgi sinxronlash muvaffaqiyatsiz ({lastRun}), eskirgan ma'lumot bilan yozilmaydi", []);

        var accounts = await db.Accounts.AsNoTracking().Where(a => a.ApplicationId == app.Id && a.Status != AccountStatus.Deleted)
            .Select(a => new AccountRow(a.Id, a.IdentityId, a.Login, a.Status, a.Kind, a.Entitlements.Select(e => e.EntitlementId).ToList(),
                a.Entitlements.Where(e => e.ViaEntitlementId == null).Select(e => e.EntitlementId).ToList(),
                new AccountTargets.State(a.DistinguishedName, a.Department, a.JobTitle, a.Branch, a.ManagerDn), a.OrgSynced))
            .ToListAsync(ct);
        var entitlements = await db.Entitlements.AsNoTracking().Where(e => e.ApplicationId == app.Id && e.DeletedAt == null)
            .ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        var expected = await (
            from ir in db.IdentityRoles
            where ir.ValidFrom <= now && (ir.ValidTo == null || ir.ValidTo > now)
            join re in db.RoleEntitlements on ir.RoleId equals re.RoleId
            where re.Entitlement.ApplicationId == app.Id && re.Entitlement.DeletedAt == null
            select new { ir.IdentityId, re.EntitlementId, ir.Role.Code }).ToListAsync(ct);
        var identityIds = accounts.Where(a => a.IdentityId is not null).Select(a => a.IdentityId!.Value)
            .Concat(expected.Select(e => e.IdentityId)).Distinct().ToList();
        var identities = await db.Identities.AsNoTracking().Where(i => identityIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Status, i.EmployeeNumber, i.FirstName, i.LastName, i.Source, i.Department, i.JobTitle, i.Branch, i.ManagerId })
            .ToListAsync(ct);

        // IdM qo'shgan a'zoliklar: (akkaunt, guruh) bo'yicha oxirgi muvaffaqiyatli amal — qo'shish
        var history = await db.ProvisioningOperations.AsNoTracking()
            .Where(o => o.ApplicationId == app.Id && o.Status == ProvisioningStatus.Succeeded && o.AccountId != null && o.EntitlementId != null
                && (o.Kind == ProvisioningKind.AddMembership || o.Kind == ProvisioningKind.RemoveMembership))
            .Select(o => new { AccountId = o.AccountId!.Value, EntitlementId = o.EntitlementId!.Value, o.Kind, o.CompletedAt }).ToListAsync(ct);
        var granted = history.GroupBy(h => (h.AccountId, h.EntitlementId))
            .Where(g => g.OrderBy(h => h.CompletedAt).Last().Kind == ProvisioningKind.AddMembership).Select(g => g.Key).ToHashSet();

        // Akkaunt holatini IdM oxirgi marta qanday o'zgartirgan: bloklagan (qaysi sababdan), yoqqan yoki yaratgan
        var lifecycleKinds = new[] { ProvisioningKind.CreateAccount, ProvisioningKind.DisableAccount, ProvisioningKind.EnableAccount };
        var lastLifecycle = (await db.ProvisioningOperations.AsNoTracking()
            .Where(o => o.ApplicationId == app.Id && o.Status == ProvisioningStatus.Succeeded && o.AccountId != null && lifecycleKinds.Contains(o.Kind))
            .Select(o => new { AccountId = o.AccountId!.Value, o.Kind, o.Reason, o.CompletedAt }).ToListAsync(ct))
            .GroupBy(o => o.AccountId).ToDictionary(g => g.Key, g => g.OrderBy(o => o.CompletedAt).Last());

        // Attestatsiyada olib tashlangan ortiqcha guruhlar (hali tizimdan olinmagan)
        var revoked = (await db.AccessReviewItems.AsNoTracking()
            .Where(r => r.Kind == AccessReviewItemKind.Entitlement && r.Decision == AccessReviewDecision.Revoke && r.RemediatedAt == null
                && r.Account!.ApplicationId == app.Id)
            .Select(r => new { AccountId = r.AccountId!.Value, EntitlementId = r.EntitlementId!.Value }).ToListAsync(ct))
            .Select(r => (r.AccountId, r.EntitlementId)).ToHashSet();

        var protectedLogins = new HashSet<string>(settings.ProtectedLogins ?? [], StringComparer.OrdinalIgnoreCase);
        bool Touchable(AccountRow a) => a.Kind != AccountKind.Service && !protectedLogins.Contains(a.Login);
        var expectedBy = expected.ToLookup(e => e.IdentityId);
        var accountsBy = accounts.Where(a => a.IdentityId is not null).ToLookup(a => a.IdentityId!.Value);
        var plan = new List<Planned>();
        var warnings = new List<string>();

        // Rahbarning shu tizimdagi akkaunti (DN) — "manager" atributi uchun; bir nechta bo'lsa faol shaxsiysi
        var managerDn = accounts.Where(a => a.IdentityId is not null && a.Kind == AccountKind.Personal && a.State.Dn is not null)
            .GroupBy(a => a.IdentityId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Status == AccountStatus.Enabled ? 0 : 1).First().State.Dn);

        foreach (var i in identities)
        {
            var mine = accountsBy[i.Id].ToList();
            var touchable = mine.Where(Touchable).ToList();

            // HR holatiga keltirish (bo'lim, lavozim, filial, rahbar, OU). Faqat HR manbali xodim: AD manbalisida AD o'zi haqiqat
            if (settings.UpdateAccounts && i.Source == IdentitySource.Hr)
                // Tizimdagi holati hali o'qilmagan akkaunt bilan solishtirib bo'lmaydi (yolg'on "— → X" takliflari bo'lardi)
                foreach (var a in touchable.Where(a => a.Kind == AccountKind.Personal && a.OrgSynced))
                {
                    var container = AccountTargets.Container(settings, i.Status, i.Branch, a.State.Dn);
                    var attributes = i.Status == IdentityStatus.Terminated ? null : AccountTargets.Attributes(settings, i.Department, i.JobTitle, i.Branch,
                        i.ManagerId is { } m ? managerDn.GetValueOrDefault(m) : null);
                    var diff = AccountTargets.Differences(settings, attributes, container, a.State);
                    if (diff.Count > 0)
                        plan.Add(new(ProvisioningKind.UpdateAccount, i.Id, a.Id, null,
                            i.Status == IdentityStatus.Terminated ? "leaver" : "mover", $"{a.Login}: {string.Join("; ", diff)}"));
                }
            var want = expectedBy[i.Id].GroupBy(e => e.EntitlementId)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(e => e.Code).Distinct().Order()));

            if (i.Status == IdentityStatus.Terminated)
            {
                foreach (var a in touchable)
                {
                    if (a.Status == AccountStatus.Enabled) plan.Add(new(ProvisioningKind.DisableAccount, i.Id, a.Id, null, "leaver", null));
                    foreach (var e in a.Direct.Where(entitlements.ContainsKey))
                        plan.Add(new(ProvisioningKind.RemoveMembership, i.Id, a.Id, e, "leaver", null));
                }
                continue;
            }
            if (i.Status == IdentityStatus.Suspended && settings.DisableSuspended)
                foreach (var a in touchable.Where(a => a.Status == AccountStatus.Enabled))
                    plan.Add(new(ProvisioningKind.DisableAccount, i.Id, a.Id, null, "suspended", null));

            if (i.Status == IdentityStatus.Active)
                foreach (var a in touchable.Where(a => a.Status == AccountStatus.Disabled))
                {
                    if (!lastLifecycle.TryGetValue(a.Id, out var last)) continue;
                    var reason = last switch
                    {
                        { Kind: ProvisioningKind.DisableAccount, Reason: "leaver" } when settings.EnableReturning => "rehire",
                        { Kind: ProvisioningKind.DisableAccount, Reason: "suspended" } when settings.EnableReturning => "resumed",
                        { Kind: ProvisioningKind.CreateAccount } when settings.EnableNewAccounts => "start-date",
                        _ => null
                    };
                    if (reason is not null) plan.Add(new(ProvisioningKind.EnableAccount, i.Id, a.Id, null, reason, null));
                }

            foreach (var a in touchable)
                foreach (var e in a.Direct.Where(e => !want.ContainsKey(e) && entitlements.ContainsKey(e)))
                {
                    if (granted.Contains((a.Id, e))) plan.Add(new(ProvisioningKind.RemoveMembership, i.Id, a.Id, e, "role-removed", null));
                    else if (revoked.Contains((a.Id, e))) plan.Add(new(ProvisioningKind.RemoveMembership, i.Id, a.Id, e, "review-revoked", null));
                }

            if (i.Status is not (IdentityStatus.Active or IdentityStatus.Pending) || want.Count == 0) continue;
            if (mine.Count == 0)
            {
                if (settings.CreateAccounts)
                    plan.Add(new(ProvisioningKind.CreateAccount, i.Id, null, null, "joiner",
                        LoginGenerator.Candidates(i.FirstName, i.LastName, settings.LoginMaxLength).FirstOrDefault()));
                else warnings.Add($"{i.EmployeeNumber}: {app.Code}'da akkaunti yo'q, {want.Count} ta guruh berilmadi (akkaunt yaratish o'chiq)");
                continue;
            }
            var held = mine.SelectMany(a => a.Entitlements).ToHashSet();
            var missing = want.Keys.Where(e => !held.Contains(e)).ToList();
            if (missing.Count == 0) continue;
            var personal = touchable.Where(a => a.Kind == AccountKind.Personal).ToList();
            var target = personal.Count == 1 ? personal[0]
                : personal.Count(a => a.Status == AccountStatus.Enabled) == 1 ? personal.Single(a => a.Status == AccountStatus.Enabled) : null;
            if (target is null)
            {
                warnings.Add($"{i.EmployeeNumber}: {app.Code}'da {(personal.Count == 0 ? "o'zgartirish mumkin bo'lgan shaxsiy akkaunt yo'q" : $"{personal.Count} ta shaxsiy akkaunt, qaysi biriga berish noaniq")}");
                continue;
            }
            foreach (var e in missing) plan.Add(new(ProvisioningKind.AddMembership, i.Id, target.Id, e, "role", want[e]));
        }

        // Faol amallar bilan solishtirish: yangilari qo'shiladi, keraksizlari bekor qilinadi, bajarilayotganiga tegilmaydi
        var planned = plan.DistinctBy(p => DedupKey(app.Id, p.Kind, p.IdentityId, p.AccountId, p.EntitlementId))
            .ToDictionary(p => DedupKey(app.Id, p.Kind, p.IdentityId, p.AccountId, p.EntitlementId));
        var active = await db.ProvisioningOperations.Where(o => o.ApplicationId == app.Id && ProvisioningOperation.ActiveStatuses.Contains(o.Status)).ToListAsync(ct);
        var activeKeys = active.Select(o => o.DedupKey).ToHashSet();
        var fresh = planned.Where(p => !activeKeys.Contains(p.Key)).ToList();
        var hold = settings.Mode == ProvisioningMode.Automatic && fresh.Count > settings.MaxAutoOperations;
        var status = settings.Mode == ProvisioningMode.Automatic && !hold ? ProvisioningStatus.Queued : ProvisioningStatus.Proposed;
        foreach (var (key, p) in fresh)
            db.ProvisioningOperations.Add(new ProvisioningOperation
            {
                ApplicationId = app.Id, Kind = p.Kind, Status = status, IdentityId = p.IdentityId, AccountId = p.AccountId, EntitlementId = p.EntitlementId,
                Reason = p.Reason, Summary = p.Summary, DedupKey = key, CreatedAt = now, NextAttemptAt = status == ProvisioningStatus.Queued ? now : null
            });
        var obsolete = active.Where(o => o.Status != ProvisioningStatus.Running && !planned.ContainsKey(o.DedupKey)).ToList();
        foreach (var o in obsolete) (o.Status, o.CompletedAt, o.NextAttemptAt) = (ProvisioningStatus.Obsolete, now, null);

        if (fresh.Count + obsolete.Count > 0)
        {
            var entries = new List<AuditEntry>
            {
                new(actorId, actorName, hold ? "provisioning.held" : "provisioning.planned", "application", app.Id.ToString(), JsonSerializer.Serialize(new
                {
                    application = app.Code, mode = settings.Mode.ToString(), status = status.ToString(),
                    added = fresh.GroupBy(f => f.Value.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                    obsolete = obsolete.Count, limit = hold ? settings.MaxAutoOperations : (int?)null
                }, Json))
            };
            await audit.SaveChangesAsync(entries, ct);
        }
        await transaction.CommitAsync(ct);
        return new PlanResult(planned.Count, fresh.Count, obsolete.Count, hold, null, warnings);
    }

    /// <summary>
    /// Tasdiqlangan arizalar: rolning barcha (amaldagi) ruxsatlari xodimda haqiqatda paydo bo'lsa — "bajarildi".
    /// Tizimga yozish o'chiq tizimlarda ham ishlaydi (administrator qo'lda bergan bo'lsa, sinxronlashdan keyin).
    /// </summary>
    public async Task<int> CompleteRequestsAsync(string actorId, string? actorName, CancellationToken ct = default)
    {
        var requests = await db.AccessRequests.Where(r => r.Status == AccessRequestStatus.Approved)
            .Include(r => r.Role).ThenInclude(r => r.Entitlements).ThenInclude(e => e.Entitlement).Include(r => r.Beneficiary).ToListAsync(ct);
        if (requests.Count == 0) return 0;
        var beneficiaries = requests.Select(r => r.BeneficiaryId).Distinct().ToList();
        var held = (await db.AccountEntitlements.Where(ae => ae.Account.IdentityId != null && beneficiaries.Contains(ae.Account.IdentityId!.Value)
                && ae.Account.Status != AccountStatus.Deleted)
            .Select(ae => new { IdentityId = ae.Account.IdentityId!.Value, ae.EntitlementId }).ToListAsync(ct))
            .Select(x => (x.IdentityId, x.EntitlementId)).ToHashSet();
        var entries = new List<AuditEntry>();
        foreach (var r in requests)
        {
            var needed = r.Role.Entitlements.Where(e => e.Entitlement.DeletedAt == null).Select(e => e.EntitlementId).ToList();
            if (!needed.All(e => held.Contains((r.BeneficiaryId, e)))) continue;
            r.Status = AccessRequestStatus.Fulfilled;
            entries.Add(new AuditEntry(actorId, actorName, "request.fulfilled", "access-request", r.Id.ToString(),
                JsonSerializer.Serialize(new { role = r.Role.Code, beneficiary = r.Beneficiary.EmployeeNumber, entitlements = needed.Count }, Json)));
        }
        if (entries.Count == 0) return 0;
        try
        {
            await audit.SaveChangesAsync(entries, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Ariza shu payt o'zgartirildi (masalan bekor qilindi): keyingi aylanishda qayta tekshiriladi
            db.ChangeTracker.Clear();
            return 0;
        }
        return entries.Count;
    }
}

/// <summary>Bir tizim uchun rejalashtirish, tasdiqlash va amalni olish navbatma-navbat (bir nechta server nusxasida ham).</summary>
public static class ProvisioningLocks
{
    private const long Salt = 0x70726f76;   // "prov"

    public static async Task AcquireAsync(IdmDbContext db, Guid applicationId, CancellationToken ct)
    {
        var key = BitConverter.ToInt64(applicationId.ToByteArray(), 0) ^ Salt;
        await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock({key})", ct);
    }
}
