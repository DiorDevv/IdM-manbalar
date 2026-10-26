using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Idm.Infrastructure.Sync;

public static class ConnectorRegistration
{
    /// <summary>
    /// Ishga tushishda: sozlangan har bir konnektor uchun tizim (Application) yozuvi bo'lishini ta'minlaydi. Bir nechta server nusxasi
    /// bir vaqtda ishga tushsa, ikkinchisi noyob indeksga urilib, yozuv allaqachon borligini qabul qiladi. Tugallanmagan
    /// sinxronlashlarni bu yerda yopilmaydi (boshqa nusxaniki bo'lishi mumkin): yurak urishi to'xtagan nusxalarnikini yetakchi yopadi.
    /// </summary>
    public static async Task EnsureApplicationsAsync(IdmDbContext db, AuditLog audit, TimeProvider time,
        IReadOnlyDictionary<string, ConnectorDefinition> connectors, CancellationToken ct = default)
    {
        foreach (var (code, definition) in connectors.Where(c => c.Value.Enabled))
        {
            if (await db.Applications.AnyAsync(a => a.Code == code, ct)) continue;
            var app = new Application
            {
                Code = code, Name = definition.Name ?? code, Description = definition.Description,
                Kind = definition.Kind, Risk = RiskLevel.High, CreatedAt = time.GetUtcNow()
            };
            db.Applications.Add(app);
            try
            {
                await audit.SaveChangesAsync(new AuditEntry(SyncRunner.SystemActor, SyncRunner.SystemActorName, "application.registered", "application",
                    app.Id.ToString(), $$"""{"code":"{{code}}","connector":"{{definition.Type}}"}"""), ct);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();   // boshqa nusxa shu payt ro'yxatdan o'tkazdi
            }
        }
    }
}
