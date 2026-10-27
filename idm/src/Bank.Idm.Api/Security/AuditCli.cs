using Bank.Idm.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Security;

/// <summary>
/// <c>dotnet Bank.Idm.Api.dll verify-audit</c>: audit xesh zanjirini tekshiradi va asosiy jadvallar sonini chiqaradi (zaxira nusxadan
/// tiklashni tekshirish, tergov). Natija: 0 — zanjir butun, 2 — buzilgan, 1 — ulanib bo'lmadi.
/// </summary>
public static class AuditCli
{
    public static async Task<int> VerifyAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("ConnectionStrings__Idm berilmagan");
            return 1;
        }
        await using var db = new IdmDbContext(new DbContextOptionsBuilder<IdmDbContext>().UseNpgsql(connectionString).Options);
        try
        {
            Console.WriteLine($"Xodimlar: {await db.Identities.CountAsync()}, akkauntlar: {await db.Accounts.CountAsync()}, " +
                $"rollar: {await db.Roles.CountAsync()}, arizalar: {await db.AccessRequests.CountAsync()}, audit: {await db.AuditEvents.CountAsync()}");
            var result = await new AuditLog(db, TimeProvider.System).VerifyChainAsync();
            if (result.IsValid)
            {
                var last = await db.AuditEvents.OrderByDescending(e => e.Id).Select(e => new { e.Id, e.OccurredAt, e.Hash }).FirstOrDefaultAsync();
                Console.WriteLine($"Audit zanjiri butun: {result.CheckedCount} yozuv; oxirgisi #{last?.Id} {last?.OccurredAt:O} {last?.Hash}");
                return 0;
            }
            Console.Error.WriteLine($"Audit zanjiri BUZILGAN: #{result.FirstBrokenId} yozuvdan boshlab ({result.CheckedCount} yozuv tekshirildi)");
            return 2;
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Bazaga ulanib bo'lmadi: {e.Message}");
            return 1;
        }
    }
}
