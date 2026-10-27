using System.Net.Http;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Bank.Idm.Api.Endpoints;
using Bank.Idm.Api.Security;
using Bank.Idm.Api.Sync;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Provisioning;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
// Sirlar katalogi (Vault Agent, Kubernetes/Docker secrets): har bir fayl — bitta kalit, nomi "Idm__Connectors__AD__Ldap__BindPassword"
if (builder.Configuration["Idm:SecretsDirectory"] is { Length: > 0 } secrets)
{
    if (!Directory.Exists(secrets)) throw new InvalidOperationException($"Idm:SecretsDirectory topilmadi: {secrets}");
    builder.Configuration.AddKeyPerFile(secrets, optional: false);
}
var options = builder.Configuration.GetSection(IdmOptions.Section).Get<IdmOptions>() ?? new IdmOptions();
builder.Services.Configure<IdmOptions>(builder.Configuration.GetSection(IdmOptions.Section));

// Operatsion buyruq (HTTP server ishga tushmaydi): zaxira nusxadan tiklangan yoki shubhali bazada audit zanjirini tekshirish.
// Ulanish — ConnectionStrings__Idm muhit o'zgaruvchisi yoki sirlar katalogidan (argumentda parol jarayonlar ro'yxatida ko'rinadi)
if (args is ["verify-audit", ..]) return await AuditCli.VerifyAsync(builder.Configuration.GetConnectionString("Idm"));

builder.Services.AddDbContext<IdmDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Idm")
    ?? throw new InvalidOperationException("ConnectionStrings:Idm sozlanmagan")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AuditLog>();
builder.Services.AddScoped<ConnectorSync>();
builder.Services.AddScoped<HrSync>();
builder.Services.AddScoped<Bank.Idm.Infrastructure.Access.BirthrightService>();
builder.Services.AddScoped<Bank.Idm.Infrastructure.Access.AccessAnalyzer>();
builder.Services.AddScoped<Bank.Idm.Infrastructure.Requests.AccessRequestService>();
builder.Services.AddScoped<ProvisioningPlanner>();
builder.Services.AddScoped<ProvisioningExecutor>();
builder.Services.AddScoped<ProvisioningService>();
builder.Services.AddScoped<Bank.Idm.Infrastructure.Reviews.AccessReviewService>();
builder.Services.AddScoped<Bank.Idm.Infrastructure.Reports.ReportBuilder>();
builder.Services.AddSingleton<IConnectorFactory, ConnectorFactory>();
builder.Services.AddSingleton<ClusterMembership>();
builder.Services.AddSingleton<ILeadership>(sp => sp.GetRequiredService<ClusterMembership>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ClusterMembership>());
builder.Services.AddSingleton<ConnectorSyncCoordinator>();
builder.Services.AddHostedService<ConnectorSyncWorker>();
builder.Services.AddSingleton<ProvisioningCoordinator>();
builder.Services.AddHostedService<ProvisioningWorker>();
builder.Services.AddScoped<Bank.Idm.Infrastructure.Siem.SiemForwarder>();
builder.Services.AddHostedService<SiemWorker>();
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

// Ishlab chiqish va testdan boshqa har qanday muhit (Production, Staging) — xavfsiz sozlama majburiy
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    var guard = ProductionGuard.Check(app.Configuration, options);
    foreach (var warning in guard.Warnings) app.Logger.LogWarning("Sozlama: {Warning}", warning);
    if (guard.Errors.Count > 0)
    {
        foreach (var error in guard.Errors) app.Logger.LogCritical("Xavfsiz bo'lmagan sozlama: {Error}", error);
        throw new InvalidOperationException($"Ishlab chiqarish sozlamasida {guard.Errors.Count} ta xavfsizlik xatosi, server ishga tushmadi: " + string.Join("; ", guard.Errors));
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdmDbContext>();
    if (options.MigrateOnStartup) await db.Database.MigrateAsync();
    var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
    await BootstrapAdminsAsync(db, audit, options.BootstrapAdmins);
    await ConnectorRegistration.EnsureApplicationsAsync(db, audit, TimeProvider.System, options.Connectors);
}

if (options.TrustedProxies.Length > 0)
{
    // Faqat ko'rsatilgan proksidan: mijozning haqiqiy IP manzili (audit) va HTTPS sxemasi
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    foreach (var proxy in options.TrustedProxies) forwarded.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    app.UseForwardedHeaders(forwarded);
}
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseIdmSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) app.MapOpenApi();
// Yuk taqsimlagich uchun: baza bilan aloqa; qaysi nusxa va yetakchimi (monitoring)
app.MapGet("/health", async (IdmDbContext db, ILeadership leadership, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok", node = leadership.NodeId, leader = leadership.IsLeader })
        : Results.Problem("Bazaga ulanib bo'lmadi", statusCode: 503));
app.MapMetrics();
app.MapIdmEndpoints();

app.Run();
return 0;

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
    try
    {
        await audit.SaveChangesAsync(missing.Select(l => new AuditEntry("system", "IdM", "portal-role.granted", "portal-role", l,
            $$"""{"role":"{{PortalRoles.Admin}}","reason":"bootstrap"}""")).ToList());
    }
    catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
    {
        db.ChangeTracker.Clear();   // boshqa server nusxasi shu payt qo'shdi
    }
}

public partial class Program;
