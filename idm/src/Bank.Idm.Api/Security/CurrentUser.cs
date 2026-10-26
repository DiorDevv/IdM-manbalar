using System.Security.Claims;
using Bank.Idm.Domain;

namespace Bank.Idm.Api.Security;

/// <summary>Joriy foydalanuvchi: login, ism va portal vakolatlari (audit va ruxsat tekshiruvi uchun).</summary>
public class CurrentUser(IHttpContextAccessor accessor, Microsoft.Extensions.Options.IOptions<IdmOptions> options)
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal();
    public string Login => Principal.FindFirstValue(options.Value.Sso.LoginClaim) ?? Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
    public string? Name => Principal.FindFirstValue("name") ?? Principal.FindFirstValue("preferred_username");
    public bool IsAdmin => Principal.IsInRole(PortalRoles.Admin);
    public bool IsAuditor => Principal.IsInRole(PortalRoles.Auditor);
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
    /// <summary>Bazada bor, lekin ikki bosqichli kirishsiz berilmagan vakolatlar.</summary>
    public IReadOnlyList<string> MfaRequiredFor => Principal.FindAll(PortalRolesClaimsTransformation.MfaRequiredClaim).Select(c => c.Value).Distinct().ToList();
    public IReadOnlyList<string> PortalRoleClaims => Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Where(r => r.StartsWith("idm.")).Distinct().ToList();
}
