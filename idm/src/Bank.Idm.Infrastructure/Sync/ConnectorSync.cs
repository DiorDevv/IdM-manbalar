using System.Text.Json;
using Bank.Idm.Connectors;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Sync;

/// <summary>
/// Tizimdan o'qilgan holatni IdM bazasi bilan solishtiradi: akkauntlar, guruhlar (ruxsatlar) va a'zolik.
/// Akkaunt tabel raqami bo'yicha xodimga bog'lanadi (korrelyatsiya). Hamma o'zgarishlar va audit yozuvlari bitta tranzaksiyada.
/// Birinchi yuklashda har bir obyekt uchun alohida yozuv emas, bitta umumiy audit yozuvi yoziladi.
/// </summary>
public class ConnectorSync(IdmDbContext db, AuditLog audit, TimeProvider time) : SyncRunner(db, audit, time)
{
    /// <param name="allowMassDelete">Administrator xavfsizlik chegarasidan oshgan o'chirishni ko'rib chiqib tasdiqlagan.</param>
    public Task<ConnectorRun> ExecuteAsync(Guid runId, IConnector connector, SyncSettings settings, string? actorName,
        bool allowMassDelete = false, CancellationToken ct = default) =>
        RunAsync(runId, actorName, (run, actor) => ApplyAsync(run, connector, settings, actor, allowMassDelete, ct), ct);

    private async Task<ConnectorRun> ApplyAsync(ConnectorRun run, IConnector connector, SyncSettings settings, Actor actor, bool allowMassDelete, CancellationToken ct)
    {
        var snapshot = await connector.ReadAsync(ct);
        var app = run.Application;
        var now = Time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var accounts = await Db.Accounts.Where(a => a.ApplicationId == app.Id).Include(a => a.Entitlements).ToListAsync(ct);
        var entitlements = await Db.Entitlements.Where(e => e.ApplicationId == app.Id).ToListAsync(ct);
        var firstLoad = await IsFirstLoadAsync(app.Id, ct);
        var events = new List<AuditEntry>();
        void Audit(string action, string targetType, Guid targetId, object details)
        {
            if (!firstLoad) events.Add(actor.Entry(action, targetType, targetId.ToString(), JsonSerializer.Serialize(details, Json)));
        }

        run.AccountsRead = snapshot.Accounts.Count;
        run.GroupsRead = snapshot.Groups.Count;

        // Xavfsizlik chegarasi: noto'g'ri OU, filtr yoki qisman javob butun bankni "o'chirib" yubormasligi kerak
        var incoming = snapshot.Accounts.Select(a => a.ExternalId).ToHashSet(StringComparer.Ordinal);
        var live = accounts.Where(a => a.Status != AccountStatus.Deleted).ToList();
        var vanished = live.Where(a => !incoming.Contains(a.ExternalId)).ToList();
        if (live.Count > 0 && vanished.Count * 100.0 / live.Count > settings.MaxDeletePercent && !allowMassDelete)
        {
            var percent = Math.Round(vanished.Count * 100.0 / live.Count, 1);
            return await FinishAsync(run.Id, ConnectorRunStatus.Aborted,
                $"{live.Count} ta akkauntdan {vanished.Count} tasi ({percent}%) tizimda topilmadi, chegara {settings.MaxDeletePercent}%. " +
                "O'zgarishlar qo'llanmadi: tizim sozlamasini tekshiring yoki administrator o'chirishni tasdiqlab qayta ishga tushirsin.",
                actor, ct, run.AccountsRead, run.GroupsRead);
        }

        // 1. Guruhlar (ruxsatlar). Yo'qolgan guruh o'chirilmaydi: rollar unga bog'langan bo'lishi mumkin
        var privileged = new HashSet<string>(settings.PrivilegedGroups ?? [], StringComparer.OrdinalIgnoreCase);
        var entByExternal = entitlements.ToDictionary(e => e.ExternalId, StringComparer.Ordinal);
        foreach (var g in snapshot.Groups)
        {
            if (!entByExternal.TryGetValue(g.ExternalId, out var e))
            {
                e = new Entitlement { ApplicationId = app.Id, ExternalId = g.ExternalId, Name = g.Name, Description = g.Description, IsPrivileged = privileged.Contains(g.Name) };
                if (e.IsPrivileged) e.Risk = RiskLevel.High;
                Db.Entitlements.Add(e);
                entByExternal[g.ExternalId] = e;
                Audit("entitlement.discovered", "entitlement", e.Id, new { application = app.Code, name = g.Name, privileged = e.IsPrivileged });
            }
            else
            {
                if (e.DeletedAt is not null) { e.DeletedAt = null; Audit("entitlement.restored", "entitlement", e.Id, new { application = app.Code, name = g.Name }); }
                if (e.Name != g.Name || e.Description != g.Description)
                {
                    Audit("entitlement.updated", "entitlement", e.Id, new { application = app.Code, oldName = e.Name, name = g.Name });
                    e.Name = g.Name; e.Description = g.Description;
                }
                if (!e.IsPrivileged && privileged.Contains(g.Name)) { e.IsPrivileged = true; e.Risk = RiskLevel.High; }
            }
            e.LastSyncedAt = now;
        }
        var incomingGroups = snapshot.Groups.Select(g => g.ExternalId).ToHashSet(StringComparer.Ordinal);
        foreach (var e in entitlements.Where(e => e.DeletedAt is null && !incomingGroups.Contains(e.ExternalId)))
        {
            e.DeletedAt = now;
            Audit("entitlement.removed", "entitlement", e.Id, new { application = app.Code, name = e.Name });
        }

        // 2. Akkauntlar va a'zolik
        var accByExternal = accounts.ToDictionary(a => a.ExternalId, StringComparer.Ordinal);
        var pairs = new List<(Account Account, ExternalAccount Source)>(snapshot.Accounts.Count);
        foreach (var x in snapshot.Accounts)
        {
            var status = x.IsEnabled ? AccountStatus.Enabled : AccountStatus.Disabled;
            if (!accByExternal.TryGetValue(x.ExternalId, out var a))
            {
                a = new Account { ApplicationId = app.Id, ExternalId = x.ExternalId, Login = x.Login, Status = status, CreatedAt = now };
                Map(a, x);
                Db.Accounts.Add(a);
                accounts.Add(a);
                run.AccountsCreated++;
                Audit("account.discovered", "account", a.Id, new { application = app.Code, login = x.Login, status = status.ToString() });
            }
            else
            {
                var changes = Changes(a, x);
                if (changes.Count > 0)
                {
                    run.AccountsUpdated++;
                    Audit("account.updated", "account", a.Id, new { application = app.Code, login = x.Login, changes });
                    Map(a, x);
                }
                if (a.Status != status)
                {
                    var action = a.Status == AccountStatus.Deleted ? "account.restored" : status == AccountStatus.Disabled ? "account.disabled" : "account.enabled";
                    if (status == AccountStatus.Disabled) run.AccountsDisabled++;
                    Audit(action, "account", a.Id, new { application = app.Code, login = x.Login, from = a.Status.ToString(), to = status.ToString() });
                    a.Status = status;
                }
            }
            a.LastSyncedAt = now;
            pairs.Add((a, x));

            var desired = x.GroupIds.Select(id => entByExternal.GetValueOrDefault(id)).OfType<Entitlement>().ToDictionary(e => e.Id);
            foreach (var current in a.Entitlements.Where(ae => !desired.ContainsKey(ae.EntitlementId)).ToList())
            {
                a.Entitlements.Remove(current);
                run.MembershipsRemoved++;
                Audit("account.entitlement-removed", "account", a.Id, new { application = app.Code, login = a.Login, group = entitlements.FirstOrDefault(e => e.Id == current.EntitlementId)?.Name });
            }
            var have = a.Entitlements.Select(ae => ae.EntitlementId).ToHashSet();
            foreach (var e in desired.Values.Where(e => !have.Contains(e.Id)))
            {
                a.Entitlements.Add(new AccountEntitlement { AccountId = a.Id, EntitlementId = e.Id, GrantedAt = now });
                run.MembershipsAdded++;
                Audit("account.entitlement-added", "account", a.Id, new { application = app.Code, login = a.Login, group = e.Name });
            }
        }
        foreach (var a in vanished)
        {
            a.Status = AccountStatus.Deleted;
            a.LastSyncedAt = now;
            run.MembershipsRemoved += a.Entitlements.Count;
            a.Entitlements.Clear();
            run.AccountsDeleted++;
            Audit("account.deleted", "account", a.Id, new { application = app.Code, login = a.Login });
        }

        // 3. Korrelyatsiya: tabel raqami bo'yicha xodimga bog'lash
        var numbers = pairs.Select(p => p.Source.EmployeeNumber).OfType<string>().Distinct().ToList();
        var linkedIds = accounts.Where(a => a.IdentityId != null).Select(a => a.IdentityId!.Value).Distinct().ToList();
        var identities = await Db.Identities.Where(i => numbers.Contains(i.EmployeeNumber) || linkedIds.Contains(i.Id)).ToListAsync(ct);
        var byNumber = identities.ToDictionary(i => i.EmployeeNumber, StringComparer.Ordinal);
        // Bitta tabel raqamida bir nechta akkaunt bo'lsa (masalan admin akkaunti), xodim ma'lumoti faol akkauntdan olinadi
        var primary = pairs.Where(p => p.Source.EmployeeNumber is not null).GroupBy(p => p.Source.EmployeeNumber!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Source.IsEnabled).First());
        foreach (var (a, x) in pairs)
        {
            if (x.EmployeeNumber is not { } number) continue;
            if (!byNumber.TryGetValue(number, out var identity))
            {
                if (!settings.CreateIdentities || primary[number].Account != a) continue;
                identity = new Identity
                {
                    EmployeeNumber = number, FirstName = x.FirstName ?? x.Login, LastName = x.LastName ?? x.DisplayName ?? x.Login,
                    Email = x.Email, Department = x.Department, JobTitle = x.JobTitle, Source = IdentitySource.ActiveDirectory,
                    Status = x.IsEnabled ? IdentityStatus.Active : IdentityStatus.Suspended, CreatedAt = now, UpdatedAt = now
                };
                Db.Identities.Add(identity);
                identities.Add(identity);
                byNumber[number] = identity;
                run.IdentitiesCreated++;
                Audit("identity.created", "identity", identity.Id, new { source = app.Code, employeeNumber = number, login = x.Login });
            }
            if (a.IdentityId != identity.Id)
            {
                Audit("account.correlated", "account", a.Id, new { application = app.Code, login = a.Login, employeeNumber = number, identityId = identity.Id, previousIdentityId = a.IdentityId });
                a.IdentityId = identity.Id;
            }
        }

        // 4. AD manbali xodimlar (HR ulanguncha): ma'lumot, holat va rahbar tizimdan yangilanadi. HR va qo'lda kiritilganlarga tegilmaydi
        var identityByAccountDn = pairs.Where(p => p.Account.IdentityId is not null && p.Source.DistinguishedName is not null)
            .GroupBy(p => p.Source.DistinguishedName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Account.IdentityId!.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var identity in identities.Where(i => i.Source == IdentitySource.ActiveDirectory))
        {
            var changes = new Dictionary<string, object?[]>();
            void Set<T>(string field, T current, T value, Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, value)) return;
                changes[field] = [current, value];
                apply(value);
            }
            if (primary.TryGetValue(identity.EmployeeNumber, out var p) && p.Account.IdentityId == identity.Id)
            {
                var x = p.Source;
                Set("firstName", identity.FirstName, x.FirstName ?? identity.FirstName, v => identity.FirstName = v);
                Set("lastName", identity.LastName, x.LastName ?? identity.LastName, v => identity.LastName = v);
                Set("email", identity.Email, x.Email, v => identity.Email = v);
                Set("department", identity.Department, x.Department, v => identity.Department = v);
                Set("jobTitle", identity.JobTitle, x.JobTitle, v => identity.JobTitle = v);
                var managerId = x.ManagerDn is not null && identityByAccountDn.TryGetValue(x.ManagerDn, out var m) && m != identity.Id ? m : (Guid?)null;
                Set("managerId", identity.ManagerId, managerId, v => identity.ManagerId = v);
                Set("status", identity.Status, x.IsEnabled ? IdentityStatus.Active : IdentityStatus.Suspended, v => identity.Status = v);
                Set("terminationDate", identity.TerminationDate, null, v => identity.TerminationDate = v);
            }
            else if (accounts.Where(a => a.IdentityId == identity.Id).All(a => a.Status == AccountStatus.Deleted))
            {
                // Tizimdagi barcha akkauntlari o'chirilgan: AD manbali xodim uchun bu ishdan ketish
                Set("status", identity.Status, IdentityStatus.Terminated, v => identity.Status = v);
                Set("terminationDate", identity.TerminationDate, identity.TerminationDate ?? today, v => identity.TerminationDate = v);
            }
            if (changes.Count == 0) continue;
            identity.UpdatedAt = now;
            if (Db.Entry(identity).State != EntityState.Added)
                Audit("identity.updated", "identity", identity.Id, new { source = app.Code, changes = changes.ToDictionary(c => c.Key, c => new { from = c.Value[0], to = c.Value[1] }) });
        }

        run.OrphanAccounts = accounts.Count(a => a.Status != AccountStatus.Deleted && a.IdentityId is null && a.Kind == AccountKind.Personal);
        run.Status = ConnectorRunStatus.Succeeded;
        run.FinishedAt = Time.GetUtcNow();
        if (snapshot.Warnings.Count > 0)
            run.Error = Truncate($"Ogohlantirishlar ({snapshot.Warnings.Count}): {string.Join("; ", snapshot.Warnings)}");
        events.Add(actor.Entry(firstLoad ? "connector.initial-load" : "connector.sync-completed", "application", app.Id.ToString(),
            JsonSerializer.Serialize(new
            {
                application = app.Code, runId = run.Id, run.AccountsRead, run.GroupsRead, run.AccountsCreated, run.AccountsUpdated,
                run.AccountsDisabled, run.AccountsDeleted, run.MembershipsAdded, run.MembershipsRemoved, run.IdentitiesCreated,
                run.OrphanAccounts, warnings = snapshot.Warnings.Count
            }, Json)));
        await AuditTrail.SaveChangesAsync(events, ct);
        return run;
    }

    private static void Map(Account a, ExternalAccount x)
    {
        a.Login = x.Login;
        a.DisplayName = x.DisplayName ?? JoinName(x);
        a.Email = x.Email;
        a.EmployeeNumber = x.EmployeeNumber;
        a.DistinguishedName = x.DistinguishedName;
    }

    private static Dictionary<string, object?> Changes(Account a, ExternalAccount x)
    {
        var changes = new Dictionary<string, object?>();
        if (a.Login != x.Login) changes["login"] = new { from = a.Login, to = x.Login };
        if (a.DisplayName != (x.DisplayName ?? JoinName(x))) changes["displayName"] = new { from = a.DisplayName, to = x.DisplayName ?? JoinName(x) };
        if (a.Email != x.Email) changes["email"] = new { from = a.Email, to = x.Email };
        if (a.EmployeeNumber != x.EmployeeNumber) changes["employeeNumber"] = new { from = a.EmployeeNumber, to = x.EmployeeNumber };
        if (a.DistinguishedName != x.DistinguishedName) changes["dn"] = new { from = a.DistinguishedName, to = x.DistinguishedName };
        return changes;
    }

    private static string? JoinName(ExternalAccount x) =>
        x.LastName is null && x.FirstName is null ? null : string.Join(' ', new[] { x.LastName, x.FirstName }.OfType<string>());

}
