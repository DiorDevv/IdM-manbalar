using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;

namespace Bank.Idm.Api.Security;

/// <summary>
/// So'rovlar chegarasi va vaqt chegarasi: bitta foydalanuvchi, skript yoki og'ir hisobot serverni band qilib qo'ymasligi uchun.
/// Chegaralar har bir server nusxasida alohida (HA'da umumiy chegara — nusxalar soniga ko'paytiriladi).
/// </summary>
public class RequestLimitOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Foydalanuvchi bir zumda yubora oladigan so'rovlar (portal sahifasi bir vaqtda 5–15 so'rov yuboradi).</summary>
    public int UserBurst { get; set; } = 200;
    /// <summary>Foydalanuvchi uchun uzoq muddatli tezlik, so'rov/soniya.</summary>
    public int UserPerSecond { get; set; } = 20;
    /// <summary>Tokensiz yoki yaroqsiz tokenli so'rovlar, IP manzil bo'yicha, daqiqada.</summary>
    public int AnonymousPerMinute { get; set; } = 120;
    /// <summary>Og'ir amallar (eksport, audit tekshiruvi, qayta hisoblash, kampaniya): bitta foydalanuvchidan bir vaqtda.</summary>
    public int HeavyPerUser { get; set; } = 1;
    /// <summary>Og'ir amallar butun server nusxasi bo'yicha bir vaqtda va navbatda kutadiganlar.</summary>
    public int HeavyConcurrent { get; set; } = 4;
    public int HeavyQueue { get; set; } = 16;
    /// <summary>Oddiy so'rovning eng uzoq vaqti, soniya (oshsa 504, bazadagi so'rov ham bekor qilinadi).</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int HeavyTimeoutSeconds { get; set; } = 300;
}

/// <summary>Og'ir amal belgisi (endpoint metama'lumoti).</summary>
public sealed class HeavyRequestAttribute;

/// <summary>Rad etilgan so'rovlar soni (Prometheus: idm_http_throttled_total), shu nusxa ishga tushgandan beri.</summary>
public sealed class RequestLimitCounters
{
    private long _user, _anonymous, _heavy;
    public void Add(bool heavy, bool anonymous)
    {
        if (heavy) Interlocked.Increment(ref _heavy);
        else if (anonymous) Interlocked.Increment(ref _anonymous);
        else Interlocked.Increment(ref _user);
    }
    public IReadOnlyList<(string Reason, long Count)> Snapshot =>
        [("user", Interlocked.Read(ref _user)), ("anonymous", Interlocked.Read(ref _anonymous)), ("heavy", Interlocked.Read(ref _heavy))];
}

public static class RequestLimits
{
    public const string HeavyPolicy = "heavy";

    /// <summary>Og'ir amal: global navbat, foydalanuvchiga bittadan, uzunroq vaqt chegarasi.</summary>
    public static TBuilder WithHeavyLimit<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new HeavyRequestAttribute());
        builder.RequireRateLimiting(HeavyPolicy);
        builder.WithRequestTimeout(HeavyPolicy);
        return builder;
    }

    public static IServiceCollection AddIdmRequestLimits(this IServiceCollection services, RequestLimitOptions o, string loginClaim)
    {
        services.AddSingleton<RequestLimitCounters>();
        services.AddRequestTimeouts(t =>
        {
            t.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(o.RequestTimeoutSeconds), TimeoutStatusCode = StatusCodes.Status504GatewayTimeout };
            t.AddPolicy(HeavyPolicy, new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(o.HeavyTimeoutSeconds), TimeoutStatusCode = StatusCodes.Status504GatewayTimeout });
        });
        // O'chirilganda ham "heavy" siyosati bo'lishi kerak (endpointlar unga havola qiladi)
        if (!o.Enabled) return services.AddRateLimiter(r => r.AddPolicy(HeavyPolicy, _ => RateLimitPartition.GetNoLimiter("off")));

        return services.AddRateLimiter(r =>
        {
            r.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            r.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                // 1) Foydalanuvchi (login) yoki IP bo'yicha umumiy tezlik
                PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                {
                    if (IsInfrastructure(ctx)) return RateLimitPartition.GetNoLimiter("infra");
                    if (Login(ctx, loginClaim) is { } login)
                        return RateLimitPartition.GetTokenBucketLimiter("u:" + login, _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = o.UserBurst, TokensPerPeriod = o.UserPerSecond, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                            QueueLimit = 0, AutoReplenishment = true
                        });
                    return RateLimitPartition.GetFixedWindowLimiter("ip:" + ctx.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = o.AnonymousPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                    });
                }),
                // 2) Og'ir amal: bitta foydalanuvchidan bir vaqtda faqat HeavyPerUser ta (takror bosish, skript)
                PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    ctx.GetEndpoint()?.Metadata.GetMetadata<HeavyRequestAttribute>() is null
                        ? RateLimitPartition.GetNoLimiter("light")
                        : RateLimitPartition.GetConcurrencyLimiter("h:" + (Login(ctx, loginClaim) ?? ctx.Connection.RemoteIpAddress?.ToString()),
                            _ => new ConcurrencyLimiterOptions { PermitLimit = o.HeavyPerUser, QueueLimit = 0 })));

            // 3) Og'ir amallar butun nusxa bo'yicha: navbat bilan (rad etish emas, kutish)
            r.AddPolicy(HeavyPolicy, _ => RateLimitPartition.GetConcurrencyLimiter("heavy", _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = o.HeavyConcurrent, QueueLimit = o.HeavyQueue, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

            r.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                var heavy = http.GetEndpoint()?.Metadata.GetMetadata<HeavyRequestAttribute>() is not null;
                var login = Login(http, loginClaim);
                http.RequestServices.GetRequiredService<RequestLimitCounters>().Add(heavy, login is null);
                var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? Math.Max(1, (int)Math.Ceiling(after.TotalSeconds)) : heavy ? 10 : 1;
                http.Response.Headers.RetryAfter = retry.ToString();
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Bank.Idm.Api.RequestLimits")
                    .LogWarning("So'rovlar chegarasi: {Login} {Ip} {Method} {Path} (og'ir: {Heavy})", login ?? "-", http.Connection.RemoteIpAddress, http.Request.Method, http.Request.Path, heavy);
                await Results.Problem(
                    heavy ? "Bu amal hali bajarilmoqda yoki server band. Bir ozdan keyin qayta urinib ko'ring."
                          : "So'rovlar juda ko'p. Bir necha soniyadan keyin qayta urinib ko'ring.",
                    statusCode: StatusCodes.Status429TooManyRequests, title: "Too Many Requests").ExecuteAsync(http);
            };
        });
    }

    private static bool IsInfrastructure(HttpContext ctx) =>
        ctx.Request.Path.StartsWithSegments("/health") || ctx.Request.Path.StartsWithSegments("/metrics");

    private static string? Login(HttpContext ctx, string claim) =>
        ctx.User.Identity?.IsAuthenticated == true ? ctx.User.FindFirstValue(claim) : null;
}
