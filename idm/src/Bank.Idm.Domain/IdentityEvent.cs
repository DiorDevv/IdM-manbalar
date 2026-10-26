namespace Bank.Idm.Domain;

/// <summary>
/// Xodim hayot siklidagi hodisa (JML): ishga kirish, lavozim o'zgarishi, ishdan ketish va h.k.
/// Haqiqat manbai (HR) o'zgarishidan yoziladi; G bosqichida shu hodisalar tizimlarda amallarni boshlaydi.
/// </summary>
public class IdentityEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdentityId { get; set; }
    public Identity Identity { get; set; } = null!;
    public IdentityEventKind Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    /// <summary>Qaysi manba aniqladi (tizim kodi, masalan "HR").</summary>
    public string Source { get; set; } = null!;
    public Guid? ConnectorRunId { get; set; }
    /// <summary>O'zgarishlar (JSON): maydon → eski va yangi qiymat.</summary>
    public string? Details { get; set; }
}

public enum IdentityEventKind
{
    Joiner = 0,
    Mover = 1,
    Leaver = 2,
    Rehire = 3,
    Suspended = 4,
    Resumed = 5
}
