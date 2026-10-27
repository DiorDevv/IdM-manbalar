using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Requests;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Reviews;

public record CampaignScope(string? Name, string? Description, string? Department, Guid? ApplicationId, bool PrivilegedOnly, DateOnly DueDate);

/// <summary>Kampaniya ishga tushsa nima bo'lishi: yozuvlar, xodimlar, tekshiruvchilar va administratorlar navbatiga tushadiganlar.</summary>
public record CampaignPreview(int Items, int Roles, int Entitlements, int Privileged, int Identities, int Reviewers, int AdminPool);

public record ReviewResult<T>(T? Value, int Status = 200, string? Error = null)
{
    public static ReviewResult<T> Fail(int status, string error) => new(default, status, error);
}

/// <summary>
/// Attestatsiya. Kampaniya qamrovidagi har bir ishlayotgan xodim uchun yozuvlar: ariza yoki qo'lda berilgan rollar va hech qaysi rol bilan
/// asoslanmagan ("ortiqcha") tizim guruhlari. Tekshiruvchi — bevosita rahbar; rahbar yo'q, ishdan ketgan yoki xodimning o'zi bo'lsa —
/// IdM administratorlari navbati. Qoidalar: o'z ruxsatini hech kim tasdiqlay olmaydi; qaror yakuniy; "olib tashlash" — rol darhol tugaydi,
/// ortiqcha guruhni tizimga yozish rejalashtiruvchisi olib tashlaydi (yozish o'chiq tizimda — administrator qo'lda, sinxronlash tasdiqlaydi).
/// </summary>
public class AccessReviewService(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    public const int MaxBatch = 500;
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<ReviewResult<CampaignPreview>> PreviewAsync(CampaignScope scope, CancellationToken ct = default)
    {
        if (await ValidateAsync(scope, ct) is { } error) return ReviewResult<CampaignPreview>.Fail(400, error);
        var items = await BuildItemsAsync(scope, ct);
        return new(Summarize(items));
    }

    public async Task<ReviewResult<AccessReviewCampaign>> CreateAsync(RequestActor actor, CampaignScope scope, CancellationToken ct = default)
    {
        if (await ValidateAsync(scope, ct) is { } error) return ReviewResult<AccessReviewCampaign>.Fail(400, error);
        var items = await BuildItemsAsync(scope, ct);
        if (items.Count == 0) return ReviewResult<AccessReviewCampaign>.Fail(400, "Qamrovda ko'rib chiqiladigan ruxsat yo'q");
        var campaign = new AccessReviewCampaign
        {
            Name = scope.Name!.Trim(), Description = Clean(scope.Description), Department = Clean(scope.Department), ApplicationId = scope.ApplicationId,
            PrivilegedOnly = scope.PrivilegedOnly, DueDate = scope.DueDate, CreatedAt = time.GetUtcNow(), CreatedBy = actor.Login, CreatedByName = actor.Name
        };
        db.AccessReviewCampaigns.Add(campaign);
        var summary = Summarize(items);
        // Kampaniya, audit va yozuvlar bitta tranzaksiyada; yozuvlar COPY bilan (butun bank — yuz minglab yozuv, EF bilan daqiqalar)
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, "review.campaign-created", "review-campaign", campaign.Id.ToString(),
                JsonSerializer.Serialize(new { name = campaign.Name, campaign.Department, campaign.ApplicationId, campaign.PrivilegedOnly, campaign.DueDate,
                    summary.Items, summary.Identities, summary.Reviewers, summary.AdminPool }, Json), actor.IpAddress, actor.CorrelationId), ct);
            await CopyItemsAsync(campaign.Id, items, ct);
            await transaction.CommitAsync(ct);
        }
        foreach (var item in items) item.CampaignId = campaign.Id;
        campaign.Items = items;
        return new(campaign, 201);
    }

    /// <summary>PostgreSQL binary COPY: joriy (EF) tranzaksiyada.</summary>
    private async Task CopyItemsAsync(Guid campaignId, IReadOnlyList<AccessReviewItem> items, CancellationToken ct)
    {
        var connection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
        await using var importer = await connection.BeginBinaryImportAsync(
            "COPY access_review_items (id, campaign_id, kind, identity_id, reviewer_id, identity_role_id, account_id, entitlement_id, " +
            "application_code, title, subtitle, is_privileged, decision) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var i in items)
        {
            await importer.StartRowAsync(ct);
            await importer.WriteAsync(i.Id, NpgsqlTypes.NpgsqlDbType.Uuid, ct);
            await importer.WriteAsync(campaignId, NpgsqlTypes.NpgsqlDbType.Uuid, ct);
            await importer.WriteAsync(i.Kind.ToString(), NpgsqlTypes.NpgsqlDbType.Varchar, ct);
            await importer.WriteAsync(i.IdentityId, NpgsqlTypes.NpgsqlDbType.Uuid, ct);
            await WriteNullableAsync(importer, i.ReviewerId, ct);
            await WriteNullableAsync(importer, i.IdentityRoleId, ct);
            await WriteNullableAsync(importer, i.AccountId, ct);
            await WriteNullableAsync(importer, i.EntitlementId, ct);
            await importer.WriteAsync(i.ApplicationCode, NpgsqlTypes.NpgsqlDbType.Varchar, ct);
            await importer.WriteAsync(i.Title, NpgsqlTypes.NpgsqlDbType.Varchar, ct);
            if (i.Subtitle is null) await importer.WriteNullAsync(ct); else await importer.WriteAsync(i.Subtitle, NpgsqlTypes.NpgsqlDbType.Varchar, ct);
            await importer.WriteAsync(i.IsPrivileged, NpgsqlTypes.NpgsqlDbType.Boolean, ct);
            await importer.WriteAsync(AccessReviewDecision.Pending.ToString(), NpgsqlTypes.NpgsqlDbType.Varchar, ct);
        }
        await importer.CompleteAsync(ct);
    }

    private static async Task WriteNullableAsync(Npgsql.NpgsqlBinaryImporter importer, Guid? value, CancellationToken ct)
    {
        if (value is { } v) await importer.WriteAsync(v, NpgsqlTypes.NpgsqlDbType.Uuid, ct);
        else await importer.WriteNullAsync(ct);
    }

    /// <summary>Bir nechta yozuv bo'yicha bitta qaror. Hammasi yoki hech biri: birortasini hal qila olmasa — xato, hech narsa o'zgarmaydi.</summary>
    public async Task<ReviewResult<int>> DecideAsync(RequestActor actor, IReadOnlyCollection<Guid> itemIds, bool keep, string? comment, CancellationToken ct = default)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0) return ReviewResult<int>.Fail(400, "Yozuv tanlanmagan");
        if (ids.Count > MaxBatch) return ReviewResult<int>.Fail(400, $"Bir martada ko'pi bilan {MaxBatch} ta yozuv");
        var text = Clean(comment);
        if (text?.Length > 2000) return ReviewResult<int>.Fail(400, "Izoh ko'pi bilan 2000 belgi");

        var items = await db.AccessReviewItems.Include(i => i.Campaign).Include(i => i.IdentityRole).ThenInclude(r => r!.Role).Include(i => i.Identity)
            .Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        if (items.Count != ids.Count) return ReviewResult<int>.Fail(404, "Yozuv topilmadi");
        if (items.Any(i => i.Campaign.Status != AccessReviewStatus.Active)) return ReviewResult<int>.Fail(409, "Kampaniya yakunlangan");
        if (items.Any(i => !CanDecide(actor, i))) return ReviewResult<int>.Fail(403, "Bu yozuvlarni siz ko'rib chiqa olmaysiz");
        if (items.Any(i => i.Decision != AccessReviewDecision.Pending)) return ReviewResult<int>.Fail(409, "Yozuv allaqachon ko'rib chiqilgan. Sahifani yangilang.");

        var now = time.GetUtcNow();
        var entries = new List<AuditEntry>();
        foreach (var item in items)
        {
            (item.Decision, item.Comment, item.DecidedAt, item.DecidedBy, item.DecidedByName) =
                (keep ? AccessReviewDecision.Keep : AccessReviewDecision.Revoke, text, now, actor.Login, actor.Name);
            entries.Add(new AuditEntry(actor.Login, actor.Name, keep ? "review.kept" : "review.revoked", "identity", item.IdentityId.ToString(),
                JsonSerializer.Serialize(new
                {
                    campaign = item.Campaign.Name, campaignId = item.CampaignId, itemId = item.Id, kind = item.Kind.ToString(), application = item.ApplicationCode,
                    name = item.Title, login = item.Kind == AccessReviewItemKind.Entitlement ? item.Subtitle : null,
                    role = item.Kind == AccessReviewItemKind.Role ? item.Subtitle : null, employeeNumber = item.Identity.EmployeeNumber, comment = text
                }, Json), actor.IpAddress, actor.CorrelationId));
            if (keep || item.Kind != AccessReviewItemKind.Role) continue;
            // Rol darhol tugaydi; uning guruhlarini rejalashtiruvchi (IdM qo'shganlarini) olib tashlaydi
            var assignment = item.IdentityRole!;
            if (assignment.ValidTo is null || assignment.ValidTo > now)
            {
                assignment.ValidTo = now;
                entries.Add(new AuditEntry(actor.Login, actor.Name, "identity.role-removed", "identity", item.IdentityId.ToString(),
                    JsonSerializer.Serialize(new { role = assignment.Role.Code, employeeNumber = item.Identity.EmployeeNumber, source = "review",
                        campaignId = item.CampaignId, comment = text }, Json), actor.IpAddress, actor.CorrelationId));
            }
            item.RemediatedAt = now;
        }
        try
        {
            await audit.SaveChangesAsync(entries, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ReviewResult<int>.Fail(409, "Yozuvlar shu payt boshqa foydalanuvchi tomonidan o'zgartirildi. Sahifani yangilang.");
        }
        return new(items.Count);
    }

    public async Task<ReviewResult<AccessReviewCampaign>> CloseAsync(RequestActor actor, Guid campaignId, bool cancel, CancellationToken ct = default)
    {
        var campaign = await db.AccessReviewCampaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null) return ReviewResult<AccessReviewCampaign>.Fail(404, "Kampaniya topilmadi");
        if (campaign.Status != AccessReviewStatus.Active) return ReviewResult<AccessReviewCampaign>.Fail(409, "Kampaniya allaqachon yopilgan");
        (campaign.Status, campaign.ClosedAt, campaign.ClosedBy) = (cancel ? AccessReviewStatus.Cancelled : AccessReviewStatus.Completed, time.GetUtcNow(), actor.Login);
        var stats = await db.AccessReviewItems.Where(i => i.CampaignId == campaignId).GroupBy(i => i.Decision)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key.ToString(), g => g.Count, ct);
        try
        {
            await audit.SaveChangesAsync(new AuditEntry(actor.Login, actor.Name, cancel ? "review.campaign-cancelled" : "review.campaign-completed",
                "review-campaign", campaign.Id.ToString(), JsonSerializer.Serialize(new { name = campaign.Name, decisions = stats }, Json),
                actor.IpAddress, actor.CorrelationId), ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ReviewResult<AccessReviewCampaign>.Fail(409, "Kampaniya shu payt o'zgartirildi. Sahifani yangilang.");
        }
        return new(campaign);
    }

    /// <summary>
    /// Olib tashlangan ortiqcha guruhlar: tizimdan haqiqatda yo'qolgani (IdM yozgan yoki administrator qo'lda olgan, sinxronlash ko'rgan)
    /// "bajarildi" deb belgilanadi. Akkaunt yoki guruh tizimdan o'chirilgan bo'lsa ham — bajarildi.
    /// </summary>
    public async Task<int> RemediateAsync(string actorId, string? actorName, CancellationToken ct = default)
    {
        var open = await db.AccessReviewItems.Include(i => i.Account).Include(i => i.Entitlement).Include(i => i.Identity)
            .Where(i => i.Kind == AccessReviewItemKind.Entitlement && i.Decision == AccessReviewDecision.Revoke && i.RemediatedAt == null)
            .ToListAsync(ct);
        if (open.Count == 0) return 0;
        var accountIds = open.Select(i => i.AccountId!.Value).Distinct().ToList();
        var held = (await db.AccountEntitlements.Where(ae => accountIds.Contains(ae.AccountId)).Select(ae => new { ae.AccountId, ae.EntitlementId }).ToListAsync(ct))
            .Select(x => (x.AccountId, x.EntitlementId)).ToHashSet();
        var now = time.GetUtcNow();
        var entries = new List<AuditEntry>();
        foreach (var item in open)
        {
            var gone = item.Account!.Status == AccountStatus.Deleted || item.Entitlement!.DeletedAt is not null
                || !held.Contains((item.AccountId!.Value, item.EntitlementId!.Value));
            if (!gone) continue;
            item.RemediatedAt = now;
            entries.Add(new AuditEntry(actorId, actorName, "review.remediated", "identity", item.IdentityId.ToString(),
                JsonSerializer.Serialize(new { campaignId = item.CampaignId, itemId = item.Id, application = item.ApplicationCode, name = item.Title,
                    login = item.Subtitle, employeeNumber = item.Identity.EmployeeNumber }, Json)));
        }
        if (entries.Count == 0) return 0;
        try
        {
            await audit.SaveChangesAsync(entries, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return 0;
        }
        return entries.Count;
    }

    /// <summary>Tekshiruvchi: belgilangan rahbar yoki (bo'sh bo'lsa) administrator; o'z ruxsatini hech kim.</summary>
    public static bool CanDecide(RequestActor actor, AccessReviewItem item)
    {
        if (actor.IdentityId is { } me && me == item.IdentityId) return false;
        return item.ReviewerId is { } reviewer ? reviewer == actor.IdentityId : actor.IsAdmin;
    }

    private async Task<string?> ValidateAsync(CampaignScope scope, CancellationToken ct)
    {
        var name = scope.Name?.Trim() ?? "";
        if (name.Length is < 3 or > 256) return "Nomi 3–256 belgi bo'lishi kerak";
        if (scope.Description?.Trim().Length > 2000) return "Izoh ko'pi bilan 2000 belgi";
        if (scope.Department?.Trim().Length > 256) return "Bo'lim nomi juda uzun";
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (scope.DueDate < today || scope.DueDate > today.AddDays(180)) return "Muddat bugundan boshlab 180 kun ichida bo'lishi kerak";
        if (scope.ApplicationId is { } app && !await db.Applications.AnyAsync(a => a.Id == app && a.Kind != ApplicationKind.HumanResources, ct))
            return "Tizim topilmadi";
        return null;
    }

    private async Task<List<AccessReviewItem>> BuildItemsAsync(CampaignScope scope, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var identityQuery = db.Identities.AsNoTracking().Where(i => i.Status == IdentityStatus.Active || i.Status == IdentityStatus.Suspended);
        if (Clean(scope.Department) is { } department)
        {
            var d = department.ToLower();
            identityQuery = identityQuery.Where(i => i.Department != null && i.Department.ToLower() == d);
        }
        var identities = await identityQuery.Select(i => new { i.Id, i.ManagerId, ManagerStatus = i.Manager == null ? (IdentityStatus?)null : i.Manager.Status })
            .ToDictionaryAsync(i => i.Id, ct);
        var ids = identities.Keys.ToList();

        var assignments = await db.IdentityRoles.AsNoTracking().Where(r => ids.Contains(r.IdentityId) && r.ValidFrom <= now && (r.ValidTo == null || r.ValidTo > now))
            .Select(r => new
            {
                r.Id, r.IdentityId, r.RoleId, r.Source, r.Role.Code, r.Role.Name, r.Role.Risk,
                Entitlements = r.Role.Entitlements.Where(e => e.Entitlement.DeletedAt == null)
                    .Select(e => new { e.EntitlementId, e.Entitlement.ApplicationId, e.Entitlement.Application.Code, e.Entitlement.IsPrivileged }).ToList()
            }).ToListAsync(ct);
        var expected = assignments.SelectMany(a => a.Entitlements.Select(e => (a.IdentityId, e.EntitlementId))).ToHashSet();
        var memberships = await db.AccountEntitlements.AsNoTracking()
            .Where(ae => ae.Account.IdentityId != null && ids.Contains(ae.Account.IdentityId!.Value) && ae.Account.Status != AccountStatus.Deleted
                && ae.Entitlement.DeletedAt == null)
            .Select(ae => new
            {
                IdentityId = ae.Account.IdentityId!.Value, ae.AccountId, ae.Account.Login, ae.EntitlementId, ae.Entitlement.Name, ae.Entitlement.IsPrivileged,
                ae.Entitlement.ApplicationId, ae.Entitlement.Application.Code
            }).ToListAsync(ct);

        Guid? Reviewer(Guid identityId)
        {
            var i = identities[identityId];
            return i.ManagerId is { } m && m != identityId && i.ManagerStatus is not (null or IdentityStatus.Terminated) ? m : null;
        }
        var items = new List<AccessReviewItem>();
        foreach (var a in assignments.Where(a => a.Source != RoleAssignmentSource.Birthright))
        {
            if (scope.ApplicationId is { } app && a.Entitlements.All(e => e.ApplicationId != app)) continue;
            var privileged = a.Risk >= RiskLevel.High || a.Entitlements.Any(e => e.IsPrivileged);
            if (scope.PrivilegedOnly && !privileged) continue;
            var apps = string.Join(", ", a.Entitlements.Select(e => e.Code).Distinct().Order());
            items.Add(new AccessReviewItem
            {
                Kind = AccessReviewItemKind.Role, IdentityId = a.IdentityId, ReviewerId = Reviewer(a.IdentityId), IdentityRoleId = a.Id,
                ApplicationCode = apps.Length == 0 ? "—" : apps.Length <= 128 ? apps : apps[..125] + "...", Title = a.Name, Subtitle = a.Code, IsPrivileged = privileged
            });
        }
        foreach (var m in memberships.Where(m => !expected.Contains((m.IdentityId, m.EntitlementId))))
        {
            if (scope.ApplicationId is { } app && m.ApplicationId != app) continue;
            if (scope.PrivilegedOnly && !m.IsPrivileged) continue;
            items.Add(new AccessReviewItem
            {
                Kind = AccessReviewItemKind.Entitlement, IdentityId = m.IdentityId, ReviewerId = Reviewer(m.IdentityId), AccountId = m.AccountId,
                EntitlementId = m.EntitlementId, ApplicationCode = m.Code, Title = m.Name, Subtitle = m.Login, IsPrivileged = m.IsPrivileged
            });
        }
        return items;
    }

    private static CampaignPreview Summarize(IReadOnlyCollection<AccessReviewItem> items) => new(items.Count,
        items.Count(i => i.Kind == AccessReviewItemKind.Role), items.Count(i => i.Kind == AccessReviewItemKind.Entitlement), items.Count(i => i.IsPrivileged),
        items.Select(i => i.IdentityId).Distinct().Count(), items.Select(i => i.ReviewerId).OfType<Guid>().Distinct().Count(), items.Count(i => i.ReviewerId is null));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
