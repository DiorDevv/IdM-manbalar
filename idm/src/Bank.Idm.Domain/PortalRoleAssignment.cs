namespace Bank.Idm.Domain;

/// <summary>
/// Portaldagi vakolat (SSO'dagi rollardan alohida): IdM'ni kim boshqaradi va kim audit qiladi.
/// Xodim va rahbar vakolatlari IdM ma'lumotlaridan avtomatik aniqlanadi, bu yerda saqlanmaydi.
/// </summary>
public class PortalRoleAssignment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>SSO logini (masalan "a.karimov").</summary>
    public string Login { get; set; } = null!;
    public PortalRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = null!;
}

public enum PortalRole
{
    /// <summary>IdM administratori: tizimlar, rollar, sozlamalar.</summary>
    Admin = 0,
    /// <summary>Auditor: hamma narsani ko'radi, hech narsani o'zgartirmaydi.</summary>
    Auditor = 1,
    /// <summary>Kadrlar bo'limi: xodimlarni ko'radi va tahrirlaydi.</summary>
    HrOperator = 2
}

public static class PortalRoles
{
    public const string Admin = "idm.admin";
    public const string Auditor = "idm.auditor";
    public const string HrOperator = "idm.hr";

    public static string ToClaim(this PortalRole role) => role switch
    {
        PortalRole.Admin => Admin,
        PortalRole.Auditor => Auditor,
        PortalRole.HrOperator => HrOperator,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
