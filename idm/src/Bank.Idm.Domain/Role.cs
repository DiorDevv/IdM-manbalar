namespace Bank.Idm.Domain;

/// <summary>Biznes roli (masalan "Kredit mutaxassisi"): bir nechta tizimdagi ruxsatlar to'plami.</summary>
public class Role
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public Guid? OwnerId { get; set; }
    public Identity? Owner { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Medium;
    public bool IsRequestable { get; set; } = true;

    public List<RoleEntitlement> Entitlements { get; set; } = [];
}

public class RoleEntitlement
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public Guid EntitlementId { get; set; }
    public Entitlement Entitlement { get; set; } = null!;
}

/// <summary>Xodimga berilgan rol: "bo'lishi kerak" holat (tizimdagi haqiqiy holat bilan solishtiriladi).</summary>
public class IdentityRole
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdentityId { get; set; }
    public Identity Identity { get; set; } = null!;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public RoleAssignmentSource Source { get; set; }
    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidTo { get; set; }
    /// <summary>Ariza orqali berilgan bo'lsa, o'sha ariza.</summary>
    public Guid? AccessRequestId { get; set; }
}

public enum RoleAssignmentSource
{
    /// <summary>Lavozim va bo'limga qarab avtomatik ("tug'ma").</summary>
    Birthright = 0,
    Request = 1,
    Manual = 2
}
