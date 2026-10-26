using System.Text.Json;
using Bank.Idm.Connectors.Hr;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Sync;

public record HrSettings(int MaxDeletePercent = 20, bool TerminateMissing = false, string TimeZone = "Asia/Tashkent");

/// <summary>
/// HR (haqiqat manbai) → IdM xodimlari. HR'dagi xodim IdM'da HR manbali bo'ladi (AD'dan yaratilgan bo'lsa ham egallanadi),
/// ma'lumotlari, holati va rahbari HR'dan olinadi. Holat o'zgarishlari JML hodisalari sifatida yoziladi.
/// Tizimlardagi akkauntlarga tegilmaydi (G bosqichi). Birinchi yuklashda hodisalar va alohida audit yozuvlari yozilmaydi.
/// </summary>
public class HrSync(IdmDbContext db, AuditLog audit, TimeProvider time) : SyncRunner(db, audit, time)
{
    private record Pending(Identity Identity, Dictionary<string, object?[]> Changes, List<IdentityEventKind> Events, bool Created);

    public Task<ConnectorRun> ExecuteAsync(Guid runId, IHrSource source, HrSettings settings, string? actorName,
        bool allowMassDelete = false, CancellationToken ct = default) =>
        RunAsync(runId, actorName, (run, actor) => ApplyAsync(run, source, settings, actor, allowMassDelete, ct), ct);

    private async Task<ConnectorRun> ApplyAsync(ConnectorRun run, IHrSource source, HrSettings settings, Actor actor, bool allowMassDelete, CancellationToken ct)
    {
        var snapshot = await source.ReadAsync(ct);
        var app = run.Application;
        var now = Time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone)).DateTime);
        var firstLoad = await IsFirstLoadAsync(app.Id, ct);
        run.AccountsRead = snapshot.Records.Count;

        var numbers = snapshot.Records.Select(r => r.EmployeeNumber).ToList();
        var identities = await Db.Identities.Where(i => numbers.Contains(i.EmployeeNumber) || i.Source == IdentitySource.Hr).ToListAsync(ct);
        var byNumber = identities.ToDictionary(i => i.EmployeeNumber, StringComparer.Ordinal);
        var inFile = numbers.ToHashSet(StringComparer.Ordinal);
        var missing = identities.Where(i => i.Source == IdentitySource.Hr && i.Status != IdentityStatus.Terminated
            && !inFile.Contains(i.EmployeeNumber) && !snapshot.SkippedEmployeeNumbers.Contains(i.EmployeeNumber)).ToList();

        // Xavfsizlik chegarasi: bitta importda ko'p xodimning ishdan ketishi odatda noto'g'ri fayl belgisi
        var leaving = snapshot.Records.Count(r => Desired(r, today) == IdentityStatus.Terminated
            && byNumber.TryGetValue(r.EmployeeNumber, out var i) && i.Status != IdentityStatus.Terminated);
        if (settings.TerminateMissing) leaving += missing.Count;
        var working = identities.Count(i => i.Status != IdentityStatus.Terminated);
        if (working > 0 && leaving * 100.0 / working > settings.MaxDeletePercent && !allowMassDelete)
        {
            var percent = Math.Round(leaving * 100.0 / working, 1);
            return await FinishAsync(run.Id, ConnectorRunStatus.Aborted,
                $"{working} ta ishlayotgan xodimdan {leaving} tasi ({percent}%) ishdan ketgan bo'lib chiqyapti, chegara {settings.MaxDeletePercent}%. " +
                "O'zgarishlar qo'llanmadi: HR faylini tekshiring yoki administrator tasdiqlab qayta ishga tushirsin.",
                actor, ct, run.AccountsRead);
        }

        var pending = new Dictionary<Guid, Pending>();
        Pending Track(Identity i, bool created = false) =>
            pending.TryGetValue(i.Id, out var p) ? p : pending[i.Id] = new Pending(i, [], [], created);
        void Set<T>(Identity i, string field, T current, T value, Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            Track(i).Changes[field] = [current, value];
            apply(value);
        }

        // 1. Xodimlar va holatlar
        foreach (var r in snapshot.Records)
        {
            var status = Desired(r, today);
            if (!byNumber.TryGetValue(r.EmployeeNumber, out var i))
            {
                i = new Identity
                {
                    EmployeeNumber = r.EmployeeNumber, FirstName = r.FirstName, LastName = r.LastName, MiddleName = r.MiddleName, Email = r.Email,
                    Department = r.Department, JobTitle = r.JobTitle, HireDate = r.HireDate, TerminationDate = r.TerminationDate,
                    Status = status, Source = IdentitySource.Hr, CreatedAt = now, UpdatedAt = now
                };
                Db.Identities.Add(i);
                byNumber[r.EmployeeNumber] = i;
                run.IdentitiesCreated++;
                var p = Track(i, created: true);
                // Ishga kirish sanasi kelmagan bo'lsa ham (Pending) hodisa hozir: akkauntlar birinchi ish kunigacha tayyorlanadi
                if (status != IdentityStatus.Terminated) p.Events.Add(IdentityEventKind.Joiner);
                continue;
            }
            // HR haqiqat manbai: AD yoki qo'lda yaratilgan xodimni egallaydi
            Set(i, "source", i.Source, IdentitySource.Hr, v => i.Source = v);
            Set(i, "firstName", i.FirstName, r.FirstName, v => i.FirstName = v);
            Set(i, "lastName", i.LastName, r.LastName, v => i.LastName = v);
            Set(i, "middleName", i.MiddleName, r.MiddleName, v => i.MiddleName = v);
            Set(i, "email", i.Email, r.Email, v => i.Email = v);
            Set(i, "hireDate", i.HireDate, r.HireDate, v => i.HireDate = v);
            Set(i, "terminationDate", i.TerminationDate, status == IdentityStatus.Terminated ? r.TerminationDate ?? i.TerminationDate ?? today : r.TerminationDate,
                v => i.TerminationDate = v);
            var moved = false;
            if (i.Department != r.Department) { Set(i, "department", i.Department, r.Department, v => i.Department = v); moved = true; }
            if (i.JobTitle != r.JobTitle) { Set(i, "jobTitle", i.JobTitle, r.JobTitle, v => i.JobTitle = v); moved = true; }
            if (Transition(i.Status, status) is { } kind) Track(i).Events.Add(kind);
            Set(i, "status", i.Status, status, v => i.Status = v);
            if (moved && status != IdentityStatus.Terminated) Track(i).Events.Add(IdentityEventKind.Mover);
        }

        // 2. Rahbarlar (hamma xodim yaratilgandan keyin, tabel raqami bo'yicha)
        var warnings = snapshot.Warnings.ToList();
        foreach (var r in snapshot.Records)
        {
            var i = byNumber[r.EmployeeNumber];
            Guid? managerId = null;
            if (r.ManagerEmployeeNumber is { } m)
            {
                if (byNumber.TryGetValue(m, out var manager)) managerId = manager.Id;
                else warnings.Add($"{r.EmployeeNumber}: rahbar {m} HR faylida ham, IdM'da ham yo'q");
            }
            if (i.ManagerId == managerId) continue;
            var created = pending.TryGetValue(i.Id, out var p) && p.Created;
            if (!created)
            {
                Track(i).Changes["managerId"] = [i.ManagerId, managerId];
                if (i.Status != IdentityStatus.Terminated && !Track(i).Events.Contains(IdentityEventKind.Mover)) Track(i).Events.Add(IdentityEventKind.Mover);
            }
            i.ManagerId = managerId;
        }

        // 3. HR'da yo'q bo'lib qolgan xodimlar
        run.IdentitiesMissing = missing.Count;
        if (missing.Count > 0)
            warnings.Add($"HR faylida yo'q {missing.Count} ta xodim: {string.Join(", ", missing.Take(20).Select(i => i.EmployeeNumber))}{(missing.Count > 20 ? "..." : "")}" +
                (settings.TerminateMissing ? " — ishdan ketgan deb belgilandi" : " — o'zgartirilmadi"));
        if (settings.TerminateMissing)
            foreach (var i in missing)
            {
                Track(i).Events.Add(IdentityEventKind.Leaver);
                Set(i, "status", i.Status, IdentityStatus.Terminated, v => i.Status = v);
                Set(i, "terminationDate", i.TerminationDate, i.TerminationDate ?? today, v => i.TerminationDate = v);
            }

        // 4. Hodisalar va audit
        var events = new List<AuditEntry>();
        foreach (var p in pending.Values)
        {
            var i = p.Identity;
            if (p.Created)
            {
                if (!firstLoad)
                    events.Add(actor.Entry("identity.created", "identity", i.Id.ToString(),
                        JsonSerializer.Serialize(new { source = app.Code, i.EmployeeNumber, status = i.Status.ToString(), i.Department, i.JobTitle }, Json)));
            }
            else if (p.Changes.Count > 0)
            {
                i.UpdatedAt = now;
                run.IdentitiesUpdated++;
                if (!firstLoad)
                    events.Add(actor.Entry("identity.updated", "identity", i.Id.ToString(), JsonSerializer.Serialize(new
                    {
                        source = app.Code, i.EmployeeNumber,
                        changes = p.Changes.ToDictionary(c => c.Key, c => new { from = Format(c.Value[0]), to = Format(c.Value[1]) })
                    }, Json)));
            }
            if (p.Events.Contains(IdentityEventKind.Leaver)) run.IdentitiesTerminated++;
            if (firstLoad) continue;
            var details = p.Changes.Count == 0 ? null
                : JsonSerializer.Serialize(p.Changes.ToDictionary(c => c.Key, c => new { from = Format(c.Value[0]), to = Format(c.Value[1]) }), Json);
            foreach (var kind in p.Events.Distinct())
                Db.IdentityEvents.Add(new IdentityEvent { IdentityId = i.Id, Kind = kind, OccurredAt = now, Source = app.Code, ConnectorRunId = run.Id, Details = details });
        }

        run.Status = ConnectorRunStatus.Succeeded;
        run.FinishedAt = Time.GetUtcNow();
        if (warnings.Count > 0) run.Error = Truncate($"Ogohlantirishlar ({warnings.Count}): {string.Join("; ", warnings)}");
        events.Add(actor.Entry(firstLoad ? "connector.initial-load" : "connector.sync-completed", "application", app.Id.ToString(),
            JsonSerializer.Serialize(new
            {
                application = app.Code, runId = run.Id, records = run.AccountsRead, run.IdentitiesCreated, run.IdentitiesUpdated,
                run.IdentitiesTerminated, run.IdentitiesMissing, warnings = warnings.Count
            }, Json)));
        await AuditTrail.SaveChangesAsync(events, ct);
        return run;
    }

    /// <summary>HR yozuvidan IdM holati: ishdan ketish sanasi kelgan yoki holat "ishdan ketgan" — Terminated; ishga kirish sanasi kelmagan — Pending.</summary>
    internal static IdentityStatus Desired(HrRecord r, DateOnly today)
    {
        if (r.Status == HrStatus.Terminated || r.TerminationDate <= today) return IdentityStatus.Terminated;
        if (r.HireDate > today) return IdentityStatus.Pending;
        return r.Status == HrStatus.Suspended ? IdentityStatus.Suspended : IdentityStatus.Active;
    }

    internal static IdentityEventKind? Transition(IdentityStatus from, IdentityStatus to) => (from, to) switch
    {
        _ when from == to => null,
        (_, IdentityStatus.Terminated) => IdentityEventKind.Leaver,
        (IdentityStatus.Terminated, _) => IdentityEventKind.Rehire,
        (_, IdentityStatus.Suspended) => IdentityEventKind.Suspended,
        (IdentityStatus.Suspended, IdentityStatus.Active) => IdentityEventKind.Resumed,
        _ => null
    };

    private static object? Format(object? value) => value is Enum e ? e.ToString() : value;
}
