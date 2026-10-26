using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Provisioning;

public enum ProvisioningAction { Approve, Retry, Dismiss }

public record ProvisioningActionResult(int Changed, int Skipped, string? Error = null);

/// <summary>
/// Administrator qarorlari: taklif qilingan amallarni tasdiqlash (navbatga), xato bo'lganlarini qayta ishga tushirish yoki yopish.
/// Har bir tizim qulf ostida (rejalashtiruvchi bilan to'qnashmaydi), har bir amal alohida auditda (kim nimani tasdiqlagan).
/// Holati mos kelmagan amallar o'tkazib yuboriladi (masalan shu payt rejalashtiruvchi keraksiz deb bekor qilgan).
/// </summary>
public class ProvisioningService(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    public const int MaxBatch = 500;
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <param name="enabledApplications">Tizimga yozish yoqilgan tizimlar: boshqalarining amallarini navbatga qo'yib bo'lmaydi.</param>
    public async Task<ProvisioningActionResult> ApplyAsync(ProvisioningAction action, IReadOnlyCollection<Guid> ids, IReadOnlySet<Guid> enabledApplications,
        string actorId, string? actorName, string? ip = null, string? correlationId = null, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new(0, 0, "Amal tanlanmagan");
        if (ids.Count > MaxBatch) return new(0, 0, $"Bir martada ko'pi bilan {MaxBatch} ta amal");
        var from = action switch
        {
            ProvisioningAction.Approve => ProvisioningStatus.Proposed,
            _ => ProvisioningStatus.Failed
        };
        var unique = ids.Distinct().ToList();
        var apps = await db.ProvisioningOperations.Where(o => unique.Contains(o.Id)).Select(o => o.ApplicationId).Distinct().OrderBy(a => a).ToListAsync(ct);
        int changed = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Qulflar bir xil tartibda (o'zaro kutib qolishdan himoya)
        foreach (var app in apps) await ProvisioningLocks.AcquireAsync(db, app, ct);
        var ops = await db.ProvisioningOperations.Include(o => o.Application).Include(o => o.Account).Include(o => o.Entitlement).Include(o => o.Identity)
            .Where(o => unique.Contains(o.Id)).ToListAsync(ct);
        var now = time.GetUtcNow();
        var entries = new List<AuditEntry>();
        foreach (var op in ops.Where(o => o.Status == from))
        {
            if (action != ProvisioningAction.Dismiss && !enabledApplications.Contains(op.ApplicationId)) continue;
            switch (action)
            {
                case ProvisioningAction.Approve:
                    (op.Status, op.NextAttemptAt, op.ApprovedBy, op.ApprovedByName, op.ApprovedAt) = (ProvisioningStatus.Queued, now, actorId, actorName, now);
                    break;
                case ProvisioningAction.Retry:
                    (op.Status, op.NextAttemptAt, op.Attempts, op.CompletedAt) = (ProvisioningStatus.Queued, now, 0, null);
                    break;
                case ProvisioningAction.Dismiss:
                    (op.Status, op.CompletedAt, op.NextAttemptAt) = (ProvisioningStatus.Cancelled, now, null);
                    break;
            }
            changed++;
            var details = JsonSerializer.Serialize(new
            {
                operationId = op.Id, application = op.Application.Code, kind = op.Kind.ToString(), op.Reason, login = op.Account?.Login ?? op.Summary,
                group = op.Entitlement?.Name, employeeNumber = op.Identity?.EmployeeNumber, lastError = action == ProvisioningAction.Approve ? null : op.LastError
            }, Json);
            var verb = action switch { ProvisioningAction.Approve => "approved", ProvisioningAction.Retry => "retried", _ => "dismissed" };
            entries.Add(op.AccountId is { } accountId
                ? new AuditEntry(actorId, actorName, $"provisioning.{verb}", "account", accountId.ToString(), details, ip, correlationId)
                : new AuditEntry(actorId, actorName, $"provisioning.{verb}", "identity", op.IdentityId?.ToString(), details, ip, correlationId));
        }
        if (changed > 0)
        {
            try
            {
                await audit.SaveChangesAsync(entries, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new(0, unique.Count, "Amallar shu payt o'zgartirildi. Sahifani yangilang.");
            }
        }
        await transaction.CommitAsync(ct);
        return new(changed, unique.Count - changed);
    }

    /// <summary>Server bajarish paytida to'xtagan: "bajarilmoqda" holatida qolgan amallar navbatga qaytadi (amallar idempotent).</summary>
    public static async Task<int> RecoverInterruptedAsync(IdmDbContext db, TimeProvider time, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        return await db.ProvisioningOperations.Where(o => o.Status == ProvisioningStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, ProvisioningStatus.Queued).SetProperty(o => o.NextAttemptAt, now)
                .SetProperty(o => o.LastError, "Server bajarish paytida qayta ishga tushdi"), ct);
    }
}
