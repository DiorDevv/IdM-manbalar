using System.Security.Claims;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Security;

/// <summary>Portal vakolatlarini IdM bazasidan oladi (SSO tokenidagi rollarga ishonilmaydi).</summary>
public class PortalRolesClaimsTransformation(IdmDbContext db, IOptions<IdmOptions> options) : IClaimsTransformation
{
    private const string Marker = "idm.roles.loaded";

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
            foreach (var role in roles) identity.AddClaim(new Claim(ClaimTypes.Role, role.ToClaim()));
        }
        clone.AddIdentity(identity);
        return clone;
    }
}
