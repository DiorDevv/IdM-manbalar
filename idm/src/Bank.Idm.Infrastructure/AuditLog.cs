using System.Security.Cryptography;
using System.Text;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure;

/// <summary>Audit uchun kim, nima, nimaga.</summary>
public record AuditEntry(
    string ActorId,
    string? ActorName,
    string Action,
    string? TargetType = null,
    string? TargetId = null,
    string? Details = null,
    string? IpAddress = null,
    string? CorrelationId = null);

public record AuditChainVerification(bool IsValid, long CheckedCount, long? FirstBrokenId);

/// <summary>
/// O'zgarishlarni audit yozuvi bilan bitta tranzaksiyada saqlaydi. Yozuvlar xesh zanjirini hosil qiladi:
/// Hash = SHA-256(PreviousHash | vaqt | kim | amal | obyekt | tafsilot). Zanjir navbatma-navbat (advisory lock) quriladi.
/// </summary>
public class AuditLog(IdmDbContext db, TimeProvider time)
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
    // pg_advisory_xact_lock uchun o'zgarmas kalit ("audit" so'zidan olingan)
    private const long ChainLockKey = 0x6175646974;

    /// <summary>Kontekstdagi barcha o'zgarishlarni va audit yozuvlarini atomar saqlaydi.</summary>
    public async Task SaveChangesAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct = default)
    {
        var ownsTransaction = db.Database.CurrentTransaction is null;
        var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock({ChainLockKey})", ct);
            var previous = await db.AuditEvents.OrderByDescending(e => e.Id).Select(e => e.Hash).FirstOrDefaultAsync(ct) ?? GenesisHash;
            foreach (var entry in entries)
            {
                var evt = new AuditEvent
                {
                    // PostgreSQL vaqtni mikrosekund aniqligida saqlaydi: xesh qayta hisoblanganda mos kelishi uchun yaxlitlaymiz
                    OccurredAt = TruncateToMicroseconds(time.GetUtcNow()),
                    ActorId = entry.ActorId,
                    ActorName = entry.ActorName,
                    Action = entry.Action,
                    TargetType = entry.TargetType,
                    TargetId = entry.TargetId,
                    Details = entry.Details,
                    IpAddress = entry.IpAddress,
                    CorrelationId = entry.CorrelationId,
                    PreviousHash = previous
                };
                evt.Hash = ComputeHash(evt);
                previous = evt.Hash;
                db.AuditEvents.Add(evt);
            }
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public Task SaveChangesAsync(AuditEntry entry, CancellationToken ct = default) => SaveChangesAsync([entry], ct);

    /// <summary>Butun zanjirni qayta hisoblaydi: o'zgartirilgan yoki o'chirilgan yozuv birinchi buzilgan Id sifatida qaytadi.</summary>
    public async Task<AuditChainVerification> VerifyChainAsync(CancellationToken ct = default)
    {
        var previous = GenesisHash;
        long count = 0;
        await foreach (var evt in db.AuditEvents.AsNoTracking().OrderBy(e => e.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            count++;
            if (evt.PreviousHash != previous || ComputeHash(evt) != evt.Hash)
                return new AuditChainVerification(false, count, evt.Id);
            previous = evt.Hash;
        }
        return new AuditChainVerification(true, count, null);
    }

    public static string ComputeHash(AuditEvent evt)
    {
        var payload = string.Join('|',
            evt.PreviousHash,
            evt.OccurredAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"),
            evt.ActorId, evt.ActorName ?? "", evt.Action,
            evt.TargetType ?? "", evt.TargetId ?? "", evt.Details ?? "",
            evt.IpAddress ?? "", evt.CorrelationId ?? "");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % 10, TimeSpan.Zero);
}
