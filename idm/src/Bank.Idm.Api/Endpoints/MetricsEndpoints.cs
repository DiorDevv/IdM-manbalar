using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bank.Idm.Api.Security;
using Bank.Idm.Api.Sync;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Endpoints;

/// <summary>
/// Prometheus metrikalari (matn formati). SSO'siz, alohida token bilan (Idm:MetricsToken, sirlar orqali): token sozlanmagan bo'lsa
/// endpoint yo'q (404). Faqat sonlar — shaxsiy ma'lumot yo'q.
/// </summary>
public static class MetricsEndpoints
{
    public static void MapMetrics(this WebApplication app) => app.MapGet("/metrics", async (HttpContext http, IOptionsMonitor<IdmOptions> options,
        IdmDbContext db, ILeadership cluster, TimeProvider time, CancellationToken ct) =>
    {
        var token = options.CurrentValue.MetricsToken;
        if (string.IsNullOrEmpty(token)) return Results.NotFound();
        var given = Encoding.UTF8.GetBytes(http.Request.Headers.Authorization.ToString());
        if (!CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes($"Bearer {token}"))) return Results.Unauthorized();

        var now = time.GetUtcNow();
        var sb = new StringBuilder();
        void Metric(string name, string help, string type, IEnumerable<(string Labels, double Value)> values)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n').Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
            foreach (var (labels, value) in values)
                sb.Append(name).Append(labels.Length == 0 ? "" : $"{{{labels}}}").Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        static string Label(string key, string value) => $"{key}=\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")}\"";

        Metric("idm_up", "IdM API ishlayapti", "gauge", [("", 1)]);
        Metric("idm_leader", "Shu nusxa yetakchimi (fon ishlari)", "gauge", [(Label("node", cluster.NodeId), cluster.IsLeader ? 1 : 0)]);
        Metric("idm_cluster_nodes", "Yurak urishi bor server nusxalari", "gauge",
            [("", await db.ClusterNodes.CountAsync(n => n.HeartbeatAt >= now.AddSeconds(-options.CurrentValue.Cluster.DeadAfterSeconds), ct))]);

        var runs = await db.Applications.AsNoTracking().Select(a => new
        {
            a.Code,
            LastSuccess = db.ConnectorRuns.Where(r => r.ApplicationId == a.Id && r.Status == ConnectorRunStatus.Succeeded).Max(r => r.FinishedAt),
            LastStatus = db.ConnectorRuns.Where(r => r.ApplicationId == a.Id).OrderByDescending(r => r.StartedAt).Select(r => (ConnectorRunStatus?)r.Status).FirstOrDefault()
        }).ToListAsync(ct);
        Metric("idm_connector_last_success_age_seconds", "Oxirgi muvaffaqiyatli sinxronlashdan beri soniya (-1 — hech qachon)", "gauge",
            runs.Select(r => (Label("application", r.Code), r.LastSuccess is { } at ? Math.Round((now - at).TotalSeconds) : -1d)));
        Metric("idm_connector_last_run_failed", "Oxirgi sinxronlash xato yoki to'xtatilgan (1)", "gauge",
            runs.Select(r => (Label("application", r.Code), r.LastStatus is ConnectorRunStatus.Failed or ConnectorRunStatus.Aborted ? 1d : 0d)));

        var operations = await db.ProvisioningOperations.Where(o => ProvisioningOperation.ActiveStatuses.Contains(o.Status))
            .GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        Metric("idm_provisioning_operations", "Faol tizimga yozish amallari holat bo'yicha", "gauge",
            ProvisioningOperation.ActiveStatuses.Select(s => (Label("status", s.ToString()), (double)(operations.FirstOrDefault(o => o.Key == s)?.Count ?? 0))));
        Metric("idm_access_requests_pending", "Ko'rib chiqilayotgan arizalar", "gauge", [("", await db.AccessRequests.CountAsync(r => r.Status == AccessRequestStatus.Pending, ct))]);
        Metric("idm_review_items_pending", "Faol kampaniyalarda ko'rib chiqilmagan ruxsatlar", "gauge",
            [("", await db.AccessReviewItems.CountAsync(i => i.Decision == AccessReviewDecision.Pending && i.Campaign.Status == AccessReviewStatus.Active, ct))]);
        Metric("idm_leavers_with_active_accounts", "Ishdan ketgan, akkaunti faol xodimlar", "gauge", [("", await ReportEndpoints.LeaversWithActiveAccounts(db).CountAsync(ct))]);
        Metric("idm_orphan_accounts", "Egasiz shaxsiy akkauntlar", "gauge",
            [("", await db.Accounts.CountAsync(a => a.IdentityId == null && a.Status != AccountStatus.Deleted && a.Kind == AccountKind.Personal, ct))]);

        var lastAudit = await db.AuditEvents.MaxAsync(e => (long?)e.Id, ct) ?? 0;
        Metric("idm_audit_last_event_id", "Oxirgi audit yozuvi raqami", "counter", [("", lastAudit)]);
        var siem = options.CurrentValue.Siem;
        if (siem.Enabled)
        {
            var checkpoint = await db.SiemCheckpoints.AsNoTracking().FirstOrDefaultAsync(c => c.Name == siem.Name, ct);
            Metric("idm_siem_lag_events", "SIEM'ga hali yuborilmagan audit yozuvlari", "gauge", [("", lastAudit - (checkpoint?.LastEventId ?? 0))]);
            Metric("idm_siem_last_success_age_seconds", "SIEM'ga oxirgi muvaffaqiyatli yuborishdan beri soniya (-1 — hech qachon)", "gauge",
                [("", checkpoint?.LastSentAt is { } sent ? Math.Round((now - sent).TotalSeconds) : -1)]);
        }
        return Results.Text(sb.ToString(), "text/plain; version=0.0.4; charset=utf-8");
    });
}
