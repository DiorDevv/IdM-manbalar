using System.Linq.Expressions;
using Bank.Idm.Api.Security;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Requests;
using Bank.Idm.Infrastructure.Reviews;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Api.Endpoints;

/// <summary>
/// Attestatsiya: rahbar o'ziga kelgan yozuvlarni ko'radi va hal qiladi (har bir xodim); kampaniyalarni ko'rish — administrator va auditor;
/// yaratish va yopish — administrator. Huquq tekshiruvi AccessReviewService'da.
/// </summary>
public static class ReviewEndpoints
{
    private const int MaxTake = 200;

    private static readonly Expression<Func<AccessReviewItem, ReviewItemDto>> ToDto = i => new ReviewItemDto(i.Id, i.CampaignId, i.Campaign.Name, i.Campaign.DueDate,
        i.Kind, i.IdentityId, i.Identity.LastName + " " + i.Identity.FirstName, i.Identity.EmployeeNumber, i.Identity.Department, i.Identity.JobTitle,
        i.Identity.Status, i.ApplicationCode, i.Title, i.Subtitle, i.IsPrivileged, i.ReviewerId,
        i.Reviewer == null ? null : i.Reviewer.LastName + " " + i.Reviewer.FirstName, i.Decision, i.Comment, i.DecidedAt, i.DecidedBy, i.DecidedByName, i.RemediatedAt);

    public static void MapReviewEndpoints(this RouteGroupBuilder api)
    {
        var reviews = api.MapGroup("/reviews");

        reviews.MapGet("/inbox", async (IdmDbContext db, CurrentUser user, CancellationToken ct) =>
        {
            var actor = await RequestEndpoints.ActorAsync(db, user, ct);
            return await Inbox(db.AccessReviewItems.AsNoTracking(), actor)
                .OrderBy(i => i.Campaign.DueDate).ThenBy(i => i.Identity.LastName).ThenBy(i => i.Identity.FirstName).ThenBy(i => i.Kind).ThenBy(i => i.Title)
                .Take(AccessReviewService.MaxBatch).Select(ToDto).ToListAsync(ct);
        });

        reviews.MapPost("/decide", async Task<Results<Ok<int>, ProblemHttpResult>> (ReviewDecisionBody body, IdmDbContext db, AccessReviewService service,
            CurrentUser user, CancellationToken ct) =>
        {
            var result = await service.DecideAsync(await RequestEndpoints.ActorAsync(db, user, ct), body.Ids ?? [], body.Keep, body.Comment, ct);
            return result.Error is null ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.Error, statusCode: result.Status);
        });

        var campaigns = reviews.MapGroup("/campaigns").RequireAuthorization(IdmEndpoints.CanReadAll);

        campaigns.MapGet("/", async (IdmDbContext db, CancellationToken ct) =>
            await Campaigns(db.AccessReviewCampaigns.OrderByDescending(c => c.CreatedAt).Take(MaxTake)).ToListAsync(ct));

        campaigns.MapGet("/{id:guid}", async Task<Results<Ok<CampaignDetails>, NotFound>> (Guid id, IdmDbContext db, CancellationToken ct) =>
        {
            var campaign = await Campaigns(db.AccessReviewCampaigns.Where(c => c.Id == id)).FirstOrDefaultAsync(ct);
            if (campaign is null) return TypedResults.NotFound();
            var reviewers = await db.AccessReviewItems.Where(i => i.CampaignId == id)
                .GroupBy(i => new { i.ReviewerId, Name = i.Reviewer == null ? null : i.Reviewer.LastName + " " + i.Reviewer.FirstName })
                .Select(g => new CampaignReviewerProgress(g.Key.ReviewerId, g.Key.Name, g.Count(), g.Count(i => i.Decision == AccessReviewDecision.Pending)))
                .ToListAsync(ct);
            return TypedResults.Ok(new CampaignDetails(campaign, reviewers.OrderByDescending(r => r.Pending).ThenBy(r => r.ReviewerName).ToList()));
        });

        campaigns.MapGet("/{id:guid}/items", async (Guid id, IdmDbContext db, AccessReviewDecision? decision, Guid? reviewerId, bool? adminPool, string? search,
            int skip = 0, int take = 50, CancellationToken ct = default) =>
        {
            (skip, take) = (Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
            var query = db.AccessReviewItems.AsNoTracking().Where(i => i.CampaignId == id);
            if (decision is not null) query = query.Where(i => i.Decision == decision);
            if (reviewerId is not null) query = query.Where(i => i.ReviewerId == reviewerId);
            if (adminPool == true) query = query.Where(i => i.ReviewerId == null);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = $"%{search.Trim()}%";
                query = query.Where(i => EF.Functions.ILike(i.Identity.LastName, s) || EF.Functions.ILike(i.Identity.FirstName, s)
                    || EF.Functions.ILike(i.Identity.EmployeeNumber, s) || EF.Functions.ILike(i.Title, s) || (i.Subtitle != null && EF.Functions.ILike(i.Subtitle, s)));
            }
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(i => i.Identity.LastName).ThenBy(i => i.Identity.FirstName).ThenBy(i => i.Kind).ThenBy(i => i.Title)
                .Skip(skip).Take(take).Select(ToDto).ToListAsync(ct);
            return new PagedResult<ReviewItemDto>(items, total, skip, take);
        });

        campaigns.MapPost("/preview", async Task<Results<Ok<CampaignPreview>, ProblemHttpResult>> (CampaignBody body, AccessReviewService service, CancellationToken ct) =>
        {
            var result = await service.PreviewAsync(Scope(body), ct);
            return result.Error is null ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.Error, statusCode: result.Status);
        }).RequireAuthorization(IdmEndpoints.CanAdminister);

        campaigns.MapPost("/", async Task<Results<Created<CampaignItem>, ProblemHttpResult>> (CampaignBody body, IdmDbContext db, AccessReviewService service,
            CurrentUser user, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(await RequestEndpoints.ActorAsync(db, user, ct), Scope(body), ct);
            if (result.Value is null) return TypedResults.Problem(result.Error, statusCode: result.Status);
            return TypedResults.Created($"/api/reviews/campaigns/{result.Value.Id}", await Campaigns(db.AccessReviewCampaigns.Where(c => c.Id == result.Value.Id)).FirstAsync(ct));
        }).RequireAuthorization(IdmEndpoints.CanAdminister);

        campaigns.MapPost("/{id:guid}/close", async Task<Results<Ok<CampaignItem>, ProblemHttpResult>> (Guid id, CloseCampaignBody body, IdmDbContext db,
            AccessReviewService service, CurrentUser user, CancellationToken ct) =>
        {
            var result = await service.CloseAsync(await RequestEndpoints.ActorAsync(db, user, ct), id, body.Cancel, ct);
            return result.Value is null ? TypedResults.Problem(result.Error, statusCode: result.Status) : TypedResults.Ok(await Campaigns(db.AccessReviewCampaigns.Where(c => c.Id == id)).FirstAsync(ct));
        }).RequireAuthorization(IdmEndpoints.CanAdminister);
    }

    /// <summary>Menga kelgan, hali hal qilinmagan yozuvlar (faol kampaniyalarda); o'z ruxsatlarim kirmaydi.</summary>
    internal static IQueryable<AccessReviewItem> Inbox(IQueryable<AccessReviewItem> query, RequestActor actor)
    {
        var me = actor.IdentityId;
        var admin = actor.IsAdmin;
        return query.Where(i => i.Decision == AccessReviewDecision.Pending && i.Campaign.Status == AccessReviewStatus.Active && i.IdentityId != me
            && (i.ReviewerId == me && me != null || i.ReviewerId == null && admin));
    }

    private static IQueryable<CampaignItem> Campaigns(IQueryable<AccessReviewCampaign> source) => source.AsNoTracking().Select(c => new CampaignItem(c.Id, c.Name,
        c.Description, c.Status, c.Department, c.Application == null ? null : c.Application.Code, c.PrivilegedOnly, c.DueDate, c.CreatedAt, c.CreatedBy,
        c.CreatedByName, c.ClosedAt, c.Items.Count, c.Items.Count(i => i.Decision == AccessReviewDecision.Keep),
        c.Items.Count(i => i.Decision == AccessReviewDecision.Revoke), c.Items.Count(i => i.Decision == AccessReviewDecision.Pending),
        c.Items.Count(i => i.Decision == AccessReviewDecision.Revoke && i.RemediatedAt == null)));

    private static CampaignScope Scope(CampaignBody b) => new(b.Name, b.Description, b.Department, b.ApplicationId, b.PrivilegedOnly, b.DueDate);
}
