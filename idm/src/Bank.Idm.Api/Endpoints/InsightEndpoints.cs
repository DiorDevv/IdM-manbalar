using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Endpoints;

public record DayValue(DateOnly Day, double Value);
/// <param name="Flows">Kunlik oqimlar (har kun uchun son, Days bilan bir tartibda): requests, jml, provisioned, provisioningFailed.</param>
/// <param name="Levels">Holat ko'rsatkichlari tarixi (snapshot'lar; ma'lumot yig'ilgan kunlargina).</param>
public record TrendsResponse(IReadOnlyList<DateOnly> Days, IReadOnlyDictionary<string, int[]> Flows, IReadOnlyDictionary<string, IReadOnlyList<DayValue>> Levels);
public record RiskMatrixResponse(IReadOnlyList<string> Departments, IReadOnlyList<string> Applications, IReadOnlyList<RiskCell> Cells, int MaxExcess);

/// <summary>Bosh sahifadagi nazorat markazi: trendlar va xavf xaritasi (admin, auditor).</summary>
public static class InsightEndpoints
{
    private static readonly TimeZoneInfo Tashkent = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");
    private const int MaxDepartments = 10;
    public const string OtherDepartment = "\u0000other";   // portal "Boshqa" deb ko'rsatadi

    private class DayCount { public DateOnly Day { get; set; } public int Count { get; set; } }

    public static void MapInsightEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/dashboard").RequireAuthorization(IdmEndpoints.CanReadAll);

        group.MapGet("/trends", async (IdmDbContext db, TimeProvider time, int days = 30, CancellationToken ct = default) =>
        {
            days = days is 7 or 30 or 90 ? days : 30;
            var today = Today(time);
            var from = today.AddDays(-(days - 1));
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), Tashkent.GetUtcOffset(from.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
            var axis = Enumerable.Range(0, days).Select(i => from.AddDays(i)).ToList();
            int[] Series(List<DayCount> rows)
            {
                var map = rows.ToDictionary(r => r.Day, r => r.Count);
                return axis.Select(d => map.GetValueOrDefault(d)).ToArray();
            }
            // Kunlar Toshkent vaqti bo'yicha (PostgreSQL'da guruhlanadi — 50 ming xodimdagi yuz minglab amal ham tez)
            var requests = await db.Database.SqlQuery<DayCount>($"""
                select (created_at at time zone 'Asia/Tashkent')::date as "Day", count(*)::int as "Count"
                from access_requests where created_at >= {start} group by 1
                """).ToListAsync(ct);
            var jml = await db.Database.SqlQuery<DayCount>($"""
                select (occurred_at at time zone 'Asia/Tashkent')::date as "Day", count(*)::int as "Count"
                from identity_events where occurred_at >= {start} group by 1
                """).ToListAsync(ct);
            var provisioned = await db.Database.SqlQuery<DayCount>($"""
                select (completed_at at time zone 'Asia/Tashkent')::date as "Day", count(*)::int as "Count"
                from provisioning_operations where status = 'Succeeded' and completed_at >= {start} group by 1
                """).ToListAsync(ct);
            var failed = await db.Database.SqlQuery<DayCount>($"""
                select (completed_at at time zone 'Asia/Tashkent')::date as "Day", count(*)::int as "Count"
                from provisioning_operations where status = 'Failed' and completed_at >= {start} group by 1
                """).ToListAsync(ct);
            var levels = (await db.MetricSnapshots.AsNoTracking().Where(s => s.Day >= from).OrderBy(s => s.Day).ToListAsync(ct))
                .GroupBy(s => s.Key).ToDictionary(g => g.Key, g => (IReadOnlyList<DayValue>)g.Select(s => new DayValue(s.Day, s.Value)).ToList());
            return new TrendsResponse(axis, new Dictionary<string, int[]>
            {
                ["requests"] = Series(requests), ["jml"] = Series(jml), ["provisioned"] = Series(provisioned), ["provisioningFailed"] = Series(failed)
            }, levels);
        });

        group.MapGet("/risk-matrix", async (AccessAnalyzer analyzer, CancellationToken ct) =>
        {
            var cells = await analyzer.RiskMatrixAsync(ct);
            // Eng xavfli bo'limlar alohida, qolganlari "Boshqa"ga yig'iladi; tizimlar — asossiz ruxsat soni bo'yicha
            var topDepartments = cells.GroupBy(c => c.Department).OrderByDescending(g => g.Sum(c => c.Excess)).ThenBy(g => g.Key)
                .Take(MaxDepartments).Select(g => g.Key).ToList();
            var folded = cells.GroupBy(c => (Department: topDepartments.Contains(c.Department) ? c.Department : OtherDepartment, c.Application))
                .Select(g => new RiskCell(g.Key.Department, g.Key.Application, g.Sum(c => c.Excess), g.Sum(c => c.Privileged), g.Sum(c => c.Identities)))
                .ToList();
            var departments = folded.Select(c => c.Department).Distinct()
                .OrderBy(d => d == OtherDepartment).ThenByDescending(d => folded.Where(c => c.Department == d).Sum(c => c.Excess)).ToList();
            var applications = folded.GroupBy(c => c.Application).OrderByDescending(g => g.Sum(c => c.Excess)).Select(g => g.Key).ToList();
            return new RiskMatrixResponse(departments, applications, folded, folded.Count == 0 ? 0 : folded.Max(c => c.Excess));
        });
    }

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Tashkent).DateTime);

    /// <summary>Bugungi holat ko'rsatkichlarini yozadi (kunda bitta qator har kalit uchun; qayta yozilsa — yangilanadi).</summary>
    public static async Task CaptureSnapshotAsync(IdmDbContext db, AccessAnalyzer analyzer, TimeProvider time, CancellationToken ct)
    {
        var day = Today(time);
        var now = time.GetUtcNow();
        var values = new Dictionary<string, double>
        {
            [MetricKeys.LeaversActive] = await ReportEndpoints.LeaversWithActiveAccounts(db).CountAsync(ct),
            [MetricKeys.OrphanAccounts] = await db.Accounts.CountAsync(a => a.IdentityId == null && a.Status != AccountStatus.Deleted && a.Kind == AccountKind.Personal, ct),
            [MetricKeys.SodOpen] = (await analyzer.SodViolationsAsync(null, ct)).Count(v => v.Exception is null),
            [MetricKeys.PendingRequests] = await db.AccessRequests.CountAsync(r => r.Status == AccessRequestStatus.Pending, ct),
            [MetricKeys.ExcessAccess] = (await analyzer.RiskMatrixAsync(ct)).Sum(c => c.Excess),
        };
        foreach (var (key, value) in values)
            await db.Database.ExecuteSqlAsync($"""
                insert into metric_snapshots (day, key, value, updated_at) values ({day}, {key}, {value}, {now})
                on conflict (day, key) do update set value = excluded.value, updated_at = excluded.updated_at
                """, ct);
    }
}
