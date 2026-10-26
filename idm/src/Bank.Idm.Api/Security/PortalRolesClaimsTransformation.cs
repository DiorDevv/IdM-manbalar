using System.Security.Claims;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Security;

/// <summary>
/// Portal vakolatlarini IdM bazasidan oladi (SSO tokenidagi rollarga ishonilmaydi). AdminMfa yoqilgan bo'lsa, imtiyozli vakolat faqat
/// token'da ikkinchi omil bo'lganda beriladi.
/// </summary>
public class PortalRolesClaimsTransformation(IdmDbContext db, IOptions<IdmOptions> options) : IClaimsTransformation
{
    private const string Marker = "idm.roles.loaded";
    /// <summary>Bazada vakolati bor, lekin MFA'siz kirgani uchun berilmagan rol.</summary>
    public const string MfaRequiredClaim = "idm.mfa-required";

    /// <summary>Token'da ruxsat etilgan ikkinchi omil (amr) yoki kirish darajasi (acr) bormi.</summary>
    public static bool HasMfa(ClaimsPrincipal principal, AdminMfaOptions mfa)
    {
        var amr = principal.FindAll("amr").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (amr.Any(a => mfa.EffectiveAmr.Contains(a, StringComparer.OrdinalIgnoreCase))) return true;
        var acr = principal.FindFirstValue("acr");
        return acr is not null && (mfa.AcceptedAcr.Contains(acr, StringComparer.Ordinal) || acr == mfa.StepUpAcr);
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.HasClaim(Marker, "1")) return principal;
        var login = principal.FindFirstValue(options.Value.Sso.LoginClaim);
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(Marker, "1"));
        // SSO'dan kelgan "idm.*" rollar olib tashlanadi: vakolat faqat IdM bazasidan
        var clone = new ClaimsPrincipal(principal.Identities.Select(i => new ClaimsIdentity(
            i.Claims.Where(c => !(c.Type == ClaimTypes.Role && c.Value.StartsWith("idm."))), i.AuthenticationType, i.NameClaimType, i.RoleClaimType)));
        if (!string.IsNullOrEmpty(login))
        {
            var roles = await db.PortalRoleAssignments.AsNoTracking().Where(r => r.Login == login).Select(r => r.Role).ToListAsync();
            var mfa = options.Value.Sso.AdminMfa;
            foreach (var role in roles.Select(r => r.ToClaim()))
            {
                // Ikkinchi omilsiz imtiyozli vakolat berilmaydi; portal sababini ko'rsatadi
                if (mfa.Required && mfa.EffectiveRoles.Contains(role) && !HasMfa(principal, mfa)) identity.AddClaim(new Claim(MfaRequiredClaim, role));
                else identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
        clone.AddIdentity(identity);
        return clone;
    }
}
