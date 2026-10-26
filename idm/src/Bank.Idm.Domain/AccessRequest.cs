namespace Bank.Idm.Domain;

/// <summary>Ruxsat so'rovi: xodim (yoki rahbari) rol so'raydi, tasdiqlash bosqichlaridan o'tadi.</summary>
public class AccessRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RequesterId { get; set; }
    public Identity Requester { get; set; } = null!;
    /// <summary>Kim uchun so'ralyapti (o'zi yoki xodimi uchun).</summary>
    public Guid BeneficiaryId { get; set; }
    public Identity Beneficiary { get; set; } = null!;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public string Justification { get; set; } = null!;
    public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Pending;
    public DateTimeOffset? ValidTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>Ariza paytida aniqlangan SoD ziddiyatlari (JSON). Bo'lsa — xavfsizlik bo'limi tasdig'i majburiy.</summary>
    public string? SodConflicts { get; set; }
    /// <summary>Optimistik qulf (PostgreSQL xmin): bir arizani ikki kishi bir vaqtda tasdiqlay olmaydi.</summary>
    public uint Version { get; set; }

    public List<Approval> Approvals { get; set; } = [];
}

public enum AccessRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
    /// <summary>Tasdiqlandi va tizimlarda bajarildi.</summary>
    Fulfilled = 4,
    Failed = 5
}

public class Approval
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AccessRequestId { get; set; }
    public AccessRequest AccessRequest { get; set; } = null!;
    /// <summary>Bosqich tartibi: 1 = rahbar, 2 = resurs egasi.</summary>
    public int Step { get; set; }
    public ApprovalStepKind Kind { get; set; }
    /// <summary>Tasdiqlovchi xodim. Bo'sh bo'lsa — IdM administratorlari navbati (rahbar yoki resurs egasi belgilanmagan).</summary>
    public Guid? ApproverId { get; set; }
    public Identity? Approver { get; set; }
    public ApprovalDecision Decision { get; set; } = ApprovalDecision.Pending;
    public string? Comment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    /// <summary>Kim qaror qildi (SSO logini; avtomatik bo'lsa "system").</summary>
    public string? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    /// <summary>Optimistik qulf: bir bosqichni ikki kishi bir vaqtda hal qila olmaydi.</summary>
    public uint Version { get; set; }
}

public enum ApprovalStepKind
{
    Manager = 0,
    ResourceOwner = 1,
    Security = 2
}

public enum ApprovalDecision
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
