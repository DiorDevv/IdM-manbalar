using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Provisioning;

public record ProvisioningSettings(ProvisioningMode Mode, bool CreateAccounts = false, int MaxAutoOperations = 20,
    IReadOnlyCollection<string>? ProtectedLogins = null, bool DisableSuspended = true, int MaxAttempts = 5, int LoginMaxLength = 20);

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
/// <item>attestatsiyada rahbar olib tashlagan ortiqcha guruh — olib tashlanadi (rol bo'yicha kerak bo'lib qolmagan bo'lsa).</item>
/// </list>
/// Xizmat akkauntlari va himoyalangan loginlar hech qachon o'zgartirilmaydi. Reja faqat oxirgi sinxronlash muvaffaqiyatli bo'lsa tuziladi
/// (eskirgan ma'lumot bilan yozilmaydi). Keraksiz bo'lib qolgan kutilayotgan amallar bekor qilinadi (Obsolete).
/// </summary>
public class ProvisioningPlanner(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private record AccountRow(Guid Id, Guid? IdentityId, string Login, AccountStatus Status, AccountKind Kind, List<Guid> Entitlements);
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

        var lastRun = await db.ConnectorRuns.Where(r => r.ApplicationId == app.Id && r.Status != ConnectorRunStatus.Running)
            .OrderByDescending(r => r.StartedAt).Select(r => (ConnectorRunStatus?)r.Status).FirstOrDefaultAsync(ct);
        if (lastRun != ConnectorRunStatus.Succeeded)
            return new PlanResult(0, 0, 0, false, lastRun is null
                ? $"{app.Code} hali sinxronlanmagan: tizim holati noma'lum"
                : $"{app.Code}: oxirgi sinxronlash muvaffaqiyatsiz ({lastRun}), eskirgan ma'lumot bilan yozilmaydi", []);

        var accounts = await db.Accounts.AsNoTracking().Where(a => a.ApplicationId == app.Id && a.Status != AccountStatus.Deleted)
            .Select(a => new AccountRow(a.Id, a.IdentityId, a.Login, a.Status, a.Kind, a.Entitlements.Select(e => e.EntitlementId).ToList()))
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
            .Select(i => new { i.Id, i.Status, i.EmployeeNumber, i.FirstName, i.LastName }).ToListAsync(ct);

        // IdM qo'shgan a'zoliklar: (akkaunt, guruh) bo'yicha oxirgi muvaffaqiyatli amal — qo'shish
        var history = await db.ProvisioningOperations.AsNoTracking()
            .Where(o => o.ApplicationId == app.Id && o.Status == ProvisioningStatus.Succeeded && o.AccountId != null && o.EntitlementId != null
                && (o.Kind == ProvisioningKind.AddMembership || o.Kind == ProvisioningKind.RemoveMembership))
            .Select(o => new { AccountId = o.AccountId!.Value, EntitlementId = o.EntitlementId!.Value, o.Kind, o.CompletedAt }).ToListAsync(ct);
        var granted = history.GroupBy(h => (h.AccountId, h.EntitlementId))
            .Where(g => g.OrderBy(h => h.CompletedAt).Last().Kind == ProvisioningKind.AddMembership).Select(g => g.Key).ToHashSet();

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

        foreach (var i in identities)
        {
            var mine = accountsBy[i.Id].ToList();
            var touchable = mine.Where(Touchable).ToList();
            var want = expectedBy[i.Id].GroupBy(e => e.EntitlementId)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(e => e.Code).Distinct().Order()));

            if (i.Status == IdentityStatus.Terminated)
            {
                foreach (var a in touchable)
                {
                    if (a.Status == AccountStatus.Enabled) plan.Add(new(ProvisioningKind.DisableAccount, i.Id, a.Id, null, "leaver", null));
                    foreach (var e in a.Entitlements.Where(entitlements.ContainsKey))
                        plan.Add(new(ProvisioningKind.RemoveMembership, i.Id, a.Id, e, "leaver", null));
                }
                continue;
            }
            if (i.Status == IdentityStatus.Suspended && settings.DisableSuspended)
                foreach (var a in touchable.Where(a => a.Status == AccountStatus.Enabled))
                    plan.Add(new(ProvisioningKind.DisableAccount, i.Id, a.Id, null, "suspended", null));

            foreach (var a in touchable)
                foreach (var e in a.Entitlements.Where(e => !want.ContainsKey(e) && entitlements.ContainsKey(e)))
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
