using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Sync;

public static class ConnectorRegistration
{
    /// <summary>
    /// Ishga tushishda: sozlangan har bir konnektor uchun tizim (Application) yozuvi bo'lishini ta'minlaydi va
    /// server to'xtab qolgani sababli "ishlayapti" holatida qolgan ishga tushishlarni yopadi (bitta nusxa uchun; HA I bosqichida).
    /// </summary>
    public static async Task EnsureApplicationsAsync(IdmDbContext db, AuditLog audit, TimeProvider time,
        IReadOnlyDictionary<string, ConnectorDefinition> connectors, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var stale = await db.ConnectorRuns.Where(r => r.Status == ConnectorRunStatus.Running).ToListAsync(ct);
        foreach (var run in stale)
        {
            run.Status = ConnectorRunStatus.Failed;
            run.Error = "Server qayta ishga tushgani uchun yakunlanmadi";
            run.FinishedAt = now;
        }
        var entries = new List<AuditEntry>();
        foreach (var (code, definition) in connectors.Where(c => c.Value.Enabled))
        {
            if (await db.Applications.AnyAsync(a => a.Code == code, ct)) continue;
            var app = new Application
            {
                Code = code, Name = definition.Name ?? code, Description = definition.Description,
                Kind = definition.Kind, Risk = RiskLevel.High, CreatedAt = now
            };
            db.Applications.Add(app);
            entries.Add(new AuditEntry(ConnectorSync.SystemActor, ConnectorSync.SystemActorName, "application.registered", "application", app.Id.ToString(),
                $$"""{"code":"{{code}}","connector":"{{definition.Type}}"}"""));
        }
        if (stale.Count > 0 || entries.Count > 0) await audit.SaveChangesAsync(entries, ct);
    }
}
