namespace Bank.Idm.Api.Security;

public static class SecurityHeaders
{
    /// <summary>API javoblari uchun: keshlamaslik, MIME sniffing va iframe'ga joylashni taqiqlash.</summary>
    public static IApplicationBuilder UseIdmSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Cache-Control"] = "no-store";
            h["X-Correlation-Id"] = ctx.TraceIdentifier;
            return Task.CompletedTask;
        });
        if (ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var incoming) && incoming.ToString() is { Length: > 0 and <= 128 } id)
            ctx.TraceIdentifier = id;
        await next();
    });
}
