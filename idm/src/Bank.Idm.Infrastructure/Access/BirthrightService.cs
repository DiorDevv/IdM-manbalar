using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Access;

public record BirthrightResult(int Assigned, int Removed, int IdentitiesChecked);

/// <summary>
/// Avtomatik ("tug'ma") rollar: qoidaga (bo'lim, lavozim) mos ishlayotgan xodimga rol beriladi, mos kelmay qolsa yoki xodim ishdan
/// ketsa — olib tashlanadi (yozuvga tugash sanasi qo'yiladi, tarix saqlanadi). Faqat Birthright manbali tayinlashlarga tegadi:
/// ariza yoki qo'lda berilgan rollar o'zgarmaydi. Har o'zgarish auditda; hammasi bitta tranzaksiyada, parallel ishga tushishdan qulf bilan himoyalangan.
/// </summary>
public class BirthrightService(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    private const long LockKey = 0x726f6c6573;   // "roles"
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<BirthrightResult> RecalculateAsync(string actorId, string? actorName, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock({LockKey})", ct);
        var now = time.GetUtcNow();

        var rules = await db.RoleRules.AsNoTracking().Select(r => new { r.RoleId, r.Department, r.JobTitle }).ToListAsync(ct);
        var roles = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Code, ct);
        var identities = await db.Identities.AsNoTracking()
            .Select(i => new { i.Id, i.EmployeeNumber, i.Department, i.JobTitle, i.Status }).ToListAsync(ct);
        var current = await db.IdentityRoles.Where(r => r.Source == RoleAssignmentSource.Birthright && r.ValidTo == null).ToListAsync(ct);
        var currentByIdentity = current.ToLookup(r => r.IdentityId);

        var entries = new List<AuditEntry>();
        int assigned = 0, removed = 0;
        foreach (var i in identities)
        {
            // Ishdan ketgan xodimda avtomatik rol qolmaydi; kutilayotgan (Pending) xodimga birinchi ish kunigacha beriladi
            var desired = i.Status == IdentityStatus.Terminated ? new HashSet<Guid>()
                : rules.Where(r => Matches(r.Department, i.Department) && Matches(r.JobTitle, i.JobTitle)).Select(r => r.RoleId).ToHashSet();
            var have = currentByIdentity[i.Id].ToList();
            foreach (var roleId in desired.Where(id => have.All(h => h.RoleId != id)))
            {
                db.IdentityRoles.Add(new IdentityRole { IdentityId = i.Id, RoleId = roleId, Source = RoleAssignmentSource.Birthright, ValidFrom = now });
                assigned++;
                entries.Add(new AuditEntry(actorId, actorName, "identity.role-assigned", "identity", i.Id.ToString(),
                    JsonSerializer.Serialize(new { role = roles[roleId], i.EmployeeNumber, source = "birthright" }, Json)));
            }
            foreach (var assignment in have.Where(h => !desired.Contains(h.RoleId)))
            {
                assignment.ValidTo = now;
                removed++;
                entries.Add(new AuditEntry(actorId, actorName, "identity.role-removed", "identity", i.Id.ToString(),
                    JsonSerializer.Serialize(new { role = roles.GetValueOrDefault(assignment.RoleId), i.EmployeeNumber, source = "birthright",
                        reason = i.Status == IdentityStatus.Terminated ? "terminated" : "rule-no-longer-matches" }, Json)));
            }
        }
        if (entries.Count > 0) await audit.SaveChangesAsync(entries, ct);
        await transaction.CommitAsync(ct);
        return new BirthrightResult(assigned, removed, identities.Count);
    }

    /// <summary>Bo'sh qoida maydoni — "har qanday"; aks holda bo'shliqlarsiz, katta-kichik harf farqsiz teng.</summary>
    public static bool Matches(string? rule, string? value) =>
        string.IsNullOrWhiteSpace(rule) || string.Equals(rule.Trim(), value?.Trim(), StringComparison.OrdinalIgnoreCase);
}
