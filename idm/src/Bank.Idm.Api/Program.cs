using System.Net.Http;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Api.Security;
using Bank.Idm.Api.Sync;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection(IdmOptions.Section).Get<IdmOptions>() ?? new IdmOptions();
builder.Services.Configure<IdmOptions>(builder.Configuration.GetSection(IdmOptions.Section));

builder.Services.AddDbContext<IdmDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Idm")
    ?? throw new InvalidOperationException("ConnectionStrings:Idm sozlanmagan")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AuditLog>();
builder.Services.AddScoped<ConnectorSync>();
builder.Services.AddScoped<HrSync>();
builder.Services.AddScoped<Bank.Idm.Infrastructure.Access.BirthrightService>();
builder.Services.AddScoped<Bank.Idm.Infrastructure.Access.AccessAnalyzer>();
builder.Services.AddScoped<Bank.Idm.Infrastructure.Requests.AccessRequestService>();
builder.Services.AddSingleton<IConnectorFactory, ConnectorFactory>();
builder.Services.AddSingleton<ConnectorSyncCoordinator>();
builder.Services.AddHostedService<ConnectorSyncWorker>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<IClaimsTransformation, PortalRolesClaimsTransformation>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = options.Sso.Authority;
    o.MapInboundClaims = false;
    o.TokenValidationParameters.ValidAudiences = options.Sso.Audiences;
    o.TokenValidationParameters.NameClaimType = options.Sso.LoginClaim;
    o.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    if (options.Sso.AllowUntrustedCertificate)
    {
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("Idm:Sso:AllowUntrustedCertificate faqat Development muhitida ruxsat etiladi");
        o.BackchannelHttpHandler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        o.RequireHttpsMetadata = false;
    }
});
builder.Services.AddAuthorizationBuilder()
    // Har qanday so'rov uchun: SSO tokenida "idm" ruxsat doirasi bo'lishi shart
    .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => HasScope(ctx.User, options.Sso.RequiredScope))
        .Build())
    .AddPolicy(IdmEndpoints.CanReadAll, p => p.RequireAuthenticatedUser().RequireAssertion(ctx => HasScope(ctx.User, options.Sso.RequiredScope))
        .RequireRole(PortalRoles.Admin, PortalRoles.Auditor))
    .AddPolicy(IdmEndpoints.CanAdminister, p => p.RequireAuthenticatedUser().RequireAssertion(ctx => HasScope(ctx.User, options.Sso.RequiredScope))
        .RequireRole(PortalRoles.Admin));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdmDbContext>();
    if (options.MigrateOnStartup) await db.Database.MigrateAsync();
    var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
    await BootstrapAdminsAsync(db, audit, options.BootstrapAdmins);
    await ConnectorRegistration.EnsureApplicationsAsync(db, audit, TimeProvider.System, options.Connectors);
}

app.UseIdmSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", async (IdmDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Problem("Bazaga ulanib bo'lmadi", statusCode: 503));
app.MapIdmEndpoints();

app.Run();

static bool HasScope(ClaimsPrincipal user, string scope) =>
    user.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(scope);

static async Task BootstrapAdminsAsync(IdmDbContext db, AuditLog audit, IEnumerable<string> logins)
{
    var missing = new List<string>();
    foreach (var login in logins.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct())
        if (!await db.PortalRoleAssignments.AnyAsync(r => r.Login == login && r.Role == PortalRole.Admin)) missing.Add(login);
    if (missing.Count == 0) return;
    foreach (var login in missing)
        db.PortalRoleAssignments.Add(new PortalRoleAssignment { Login = login, Role = PortalRole.Admin, CreatedBy = "system" });
    await audit.SaveChangesAsync(missing.Select(l => new AuditEntry("system", "IdM", "portal-role.granted", "portal-role", l,
        $$"""{"role":"{{PortalRoles.Admin}}","reason":"bootstrap"}""")).ToList());
}

public partial class Program;
