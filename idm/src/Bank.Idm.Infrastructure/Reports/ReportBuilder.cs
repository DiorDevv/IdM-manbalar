using System.Globalization;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Access;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Reports;

/// <summary>Eksport qilinadigan jadval: Excel fayli va portalning chop etish (PDF) ko'rinishi uchun umumiy.</summary>
public record ReportTable(string Key, string Title, string? Subtitle, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Nazorat hisobotlari (auditor va regulyator uchun): kim nimaga kirish huquqiga ega, imtiyozli akkauntlar, SoD buzilishlari,
/// egasiz akkauntlar, ishdan ketgan xodimlarning faol akkauntlari, attestatsiya natijasi. Hammasi joriy holat bo'yicha (o'qishning o'zi).
/// </summary>
public class ReportBuilder(IdmDbContext db, AccessAnalyzer analyzer, TimeProvider time)
{
    /// <summary>Excel varag'i chegarasi (1 048 576) ichida.</summary>
    public const int MaxRows = 1_000_000;
    /// <summary>Chop etish (brauzerda jadval): undan katta hisobot — faqat Excel.</summary>
    public const int MaxPrintRows = 20_000;
    public static readonly string[] Keys = ["access", "privileged", "sod", "orphans", "leavers", "campaign"];
    private static readonly TimeZoneInfo Tashkent = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    /// <returns>null — bunday hisobot (yoki kampaniya) yo'q.</returns>
    public async Task<ReportTable?> BuildAsync(string key, Guid? campaignId, CancellationToken ct = default) => key switch
    {
        "access" => await AccessAsync(ct),
        "privileged" => await PrivilegedAsync(ct),
        "sod" => await SodAsync(ct),
        "orphans" => await OrphansAsync(ct),
        "leavers" => await LeaversAsync(ct),
        "campaign" when campaignId is { } id => await CampaignAsync(id, ct),
        _ => null
    };

    private async Task<ReportTable> AccessAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var rows = await db.AccountEntitlements.AsNoTracking()
            .Where(ae => ae.Account.Status != AccountStatus.Deleted && ae.Entitlement.DeletedAt == null)
            .Select(ae => new
            {
                ae.Account.IdentityId, Number = ae.Account.Identity == null ? null : ae.Account.Identity.EmployeeNumber,
                Name = ae.Account.Identity == null ? null : ae.Account.Identity.LastName + " " + ae.Account.Identity.FirstName,
                Department = ae.Account.Identity == null ? null : ae.Account.Identity.Department, JobTitle = ae.Account.Identity == null ? null : ae.Account.Identity.JobTitle,
                IdentityStatus = ae.Account.Identity == null ? (IdentityStatus?)null : ae.Account.Identity.Status,
                App = ae.Account.Application.Code, ae.Account.Login, AccountStatus = ae.Account.Status, ae.EntitlementId, Group = ae.Entitlement.Name, ae.Entitlement.IsPrivileged
            }).ToListAsync(ct);
        Limit(rows.Count);
        var via = (await (from ir in db.IdentityRoles
                          where ir.ValidFrom <= now && (ir.ValidTo == null || ir.ValidTo > now)
                          join re in db.RoleEntitlements on ir.RoleId equals re.RoleId
                          select new { ir.IdentityId, re.EntitlementId, ir.Role.Code }).ToListAsync(ct))
            .ToLookup(x => (x.IdentityId, x.EntitlementId), x => x.Code);
        return Table("access", "Kirish huquqlari", "Har bir akkauntning tizimdagi guruhlari va ularning asosi (rol)",
            ["Tabel raqami", "Xodim", "Bo'lim", "Lavozim", "Xodim holati", "Tizim", "Login", "Akkaunt holati", "Guruh", "Imtiyozli", "Asos (rol)"],
            rows.OrderBy(r => r.Name ?? "￿").ThenBy(r => r.App).ThenBy(r => r.Login).ThenBy(r => r.Group)
                .Select(r => Row(r.Number, r.Name ?? "egasiz akkaunt", r.Department, r.JobTitle, Status(r.IdentityStatus), r.App, r.Login, Status(r.AccountStatus),
                    r.Group, YesNo(r.IsPrivileged),
                    r.IdentityId is { } i && via[(i, r.EntitlementId)].Any() ? string.Join(", ", via[(i, r.EntitlementId)].Distinct().Order()) : "asossiz (ortiqcha)")));
    }

    private async Task<ReportTable> PrivilegedAsync(CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.Status != AccountStatus.Deleted && (a.Kind == AccountKind.Privileged || a.Entitlements.Any(e => e.Entitlement.IsPrivileged && e.Entitlement.DeletedAt == null)))
            .Select(a => new
            {
                App = a.Application.Code, a.Login, a.Kind, a.Status, a.LastSyncedAt,
                Number = a.Identity == null ? null : a.Identity.EmployeeNumber, Name = a.Identity == null ? null : a.Identity.LastName + " " + a.Identity.FirstName,
                OwnerStatus = a.Identity == null ? (IdentityStatus?)null : a.Identity.Status,
                Groups = a.Entitlements.Where(e => e.Entitlement.IsPrivileged && e.Entitlement.DeletedAt == null).Select(e => e.Entitlement.Name).ToList()
            }).ToListAsync(ct);
        Limit(accounts.Count);
        static string? Risk(IdentityStatus? owner, AccountStatus status) => status != AccountStatus.Enabled ? null
            : owner is null ? "egasiz imtiyozli akkaunt" : owner == IdentityStatus.Terminated ? "egasi ishdan ketgan" : owner == IdentityStatus.Suspended ? "egasi to'xtatilgan" : null;
        return Table("privileged", "Imtiyozli akkauntlar", "Imtiyozli turdagi yoki imtiyozli guruhdagi akkauntlar",
            ["Tizim", "Login", "Turi", "Akkaunt holati", "Tabel raqami", "Egasi", "Egasi holati", "Imtiyozli guruhlar", "Xavf", "Oxirgi sinxronlash"],
            accounts.OrderBy(a => Risk(a.OwnerStatus, a.Status) is null).ThenBy(a => a.App).ThenBy(a => a.Login)
                .Select(a => Row(a.App, a.Login, Kind(a.Kind), Status(a.Status), a.Number, a.Name, Status(a.OwnerStatus), string.Join(", ", a.Groups.Order()),
                    Risk(a.OwnerStatus, a.Status), Date(a.LastSyncedAt))));
    }

    private async Task<ReportTable> SodAsync(CancellationToken ct)
    {
        var violations = await analyzer.SodViolationsAsync(null, ct);
        var ids = violations.Select(v => v.IdentityId).Distinct().ToList();
        var people = await db.Identities.AsNoTracking().Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => new { i.EmployeeNumber, Name = i.LastName + " " + i.FirstName, i.Department, i.Status }, ct);
        return Table("sod", "SoD buzilishlari", "Bir xodimda bir vaqtda bo'lishi mumkin bo'lmagan ruxsatlar (tizimdagi haqiqiy holat)",
            ["Qoida", "Qoida nomi", "Xavf", "Tabel raqami", "Xodim", "Bo'lim", "Xodim holati", "A tomon", "B tomon"],
            violations.Select(v => Row(v.RuleCode, v.RuleName, Risk(v.Risk), people[v.IdentityId].EmployeeNumber, people[v.IdentityId].Name,
                people[v.IdentityId].Department, Status(people[v.IdentityId].Status), string.Join(", ", v.LeftEntitlements), string.Join(", ", v.RightEntitlements))));
    }

    private async Task<ReportTable> OrphansAsync(CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.IdentityId == null && a.Status != AccountStatus.Deleted && a.Kind == AccountKind.Personal)
            .OrderBy(a => a.Application.Code).ThenBy(a => a.Login)
            .Select(a => new { App = a.Application.Code, a.Login, a.DisplayName, a.EmployeeNumber, a.Status, a.LastSyncedAt, Groups = a.Entitlements.Count })
            .ToListAsync(ct);
        Limit(accounts.Count);
        return Table("orphans", "Egasiz akkauntlar", "Hech qaysi xodimga bog'lanmagan shaxsiy akkauntlar",
            ["Tizim", "Login", "Ism (tizimda)", "Tabel raqami (tizimda)", "Holat", "Guruhlar soni", "Oxirgi sinxronlash"],
            accounts.Select(a => Row(a.App, a.Login, a.DisplayName, a.EmployeeNumber, Status(a.Status), a.Groups.ToString(CultureInfo.InvariantCulture), Date(a.LastSyncedAt))));
    }

    private async Task<ReportTable> LeaversAsync(CancellationToken ct)
    {
        var rows = await db.Accounts.AsNoTracking()
            .Where(a => a.Status == AccountStatus.Enabled && a.Identity != null && a.Identity.Status == IdentityStatus.Terminated)
            .OrderBy(a => a.Identity!.TerminationDate).ThenBy(a => a.Identity!.LastName)
            .Select(a => new { a.Identity!.EmployeeNumber, Name = a.Identity.LastName + " " + a.Identity.FirstName, a.Identity.Department, a.Identity.TerminationDate,
                App = a.Application.Code, a.Login, a.Kind, a.LastSyncedAt })
            .ToListAsync(ct);
        return Table("leavers", "Ishdan ketgan, akkaunti faol", "HR bo'yicha ishdan ketgan, lekin tizimga hali kira oladigan xodimlar",
            ["Tabel raqami", "Xodim", "Bo'lim", "Ketgan sana", "Tizim", "Login", "Turi", "Oxirgi sinxronlash"],
            rows.Select(r => Row(r.EmployeeNumber, r.Name, r.Department, r.TerminationDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), r.App, r.Login,
                Kind(r.Kind), Date(r.LastSyncedAt))));
    }

    private async Task<ReportTable?> CampaignAsync(Guid id, CancellationToken ct)
    {
        var campaign = await db.AccessReviewCampaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (campaign is null) return null;
        var items = await db.AccessReviewItems.AsNoTracking().Where(i => i.CampaignId == id)
            .OrderBy(i => i.Identity.LastName).ThenBy(i => i.Identity.FirstName).ThenBy(i => i.Kind).ThenBy(i => i.Title)
            .Select(i => new
            {
                i.Identity.EmployeeNumber, Name = i.Identity.LastName + " " + i.Identity.FirstName, i.Identity.Department, i.Kind, i.ApplicationCode, i.Title, i.Subtitle,
                i.IsPrivileged, Reviewer = i.Reviewer == null ? null : i.Reviewer.LastName + " " + i.Reviewer.FirstName, i.Decision, i.Comment,
                By = i.DecidedByName ?? i.DecidedBy, i.DecidedAt, i.RemediatedAt
            }).ToListAsync(ct);
        Limit(items.Count);
        var decided = items.Count(i => i.Decision != AccessReviewDecision.Pending);
        return Table("campaign", $"Attestatsiya: {campaign.Name}",
            $"Muddat {campaign.DueDate:dd.MM.yyyy} · {Campaign(campaign.Status)} · ko'rib chiqildi {decided} / {items.Count}",
            ["Tabel raqami", "Xodim", "Bo'lim", "Turi", "Tizim", "Nomi", "Rol kodi / login", "Imtiyozli", "Tekshiruvchi", "Qaror", "Izoh", "Kim qaror qildi",
                "Qachon", "Olib tashlandi"],
            items.Select(i => Row(i.EmployeeNumber, i.Name, i.Department, i.Kind == AccessReviewItemKind.Role ? "Rol" : "Ortiqcha guruh", i.ApplicationCode, i.Title,
                i.Subtitle, YesNo(i.IsPrivileged), i.Reviewer ?? "IdM administratorlari",
                i.Decision switch { AccessReviewDecision.Keep => "Qoldirildi", AccessReviewDecision.Revoke => "Olib tashlandi", _ => "Ko'rib chiqilmagan" },
                i.Comment, i.By, Date(i.DecidedAt), i.Decision == AccessReviewDecision.Revoke ? i.RemediatedAt is null ? "kutilmoqda" : Date(i.RemediatedAt) : null)));
    }

    private ReportTable Table(string key, string title, string? subtitle, string[] columns, IEnumerable<IReadOnlyList<string?>> rows) =>
        new(key, title, subtitle, columns, rows.ToList(), time.GetUtcNow());

    private static IReadOnlyList<string?> Row(params string?[] values) => values;

    private static void Limit(int count)
    {
        if (count > MaxRows) throw new InvalidOperationException($"Hisobot {MaxRows} qatordan katta ({count}): qamrovni toraytiring");
    }

    private static string? Date(DateTimeOffset? value) =>
        value is { } v ? TimeZoneInfo.ConvertTime(v, Tashkent).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : null;

    private static string YesNo(bool value) => value ? "ha" : "";

    private static string? Status(IdentityStatus? s) => s switch
    {
        IdentityStatus.Pending => "Kutilmoqda", IdentityStatus.Active => "Faol", IdentityStatus.Suspended => "To'xtatilgan", IdentityStatus.Terminated => "Ishdan ketgan",
        _ => null
    };

    private static string Status(AccountStatus s) => s switch { AccountStatus.Enabled => "Faol", AccountStatus.Disabled => "Bloklangan", _ => "O'chirilgan" };

    private static string Kind(AccountKind k) => k switch { AccountKind.Service => "Xizmat", AccountKind.Privileged => "Imtiyozli", _ => "Shaxsiy" };

    private static string Risk(RiskLevel r) => r switch { RiskLevel.Low => "Past", RiskLevel.Medium => "O'rta", RiskLevel.High => "Yuqori", _ => "Kritik" };

    private static string Campaign(AccessReviewStatus s) => s switch { AccessReviewStatus.Active => "faol", AccessReviewStatus.Completed => "yakunlangan", _ => "bekor qilingan" };
}
