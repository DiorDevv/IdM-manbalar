namespace Bank.Idm.Domain;

/// <summary>
/// O'rinbosarlik (masalan ta'til paytida): muddat davomida o'rinbosar (Delegate) shu xodimga (Delegator) tushgan ariza bosqichlari va
/// attestatsiya yozuvlarini hal qila oladi. Zanjir ishlamaydi (o'rinbosarning o'rinbosari — yo'q); o'z arizasi yoki o'z ruxsatini
/// o'rinbosar ham tasdiqlay olmaydi. Qaror auditda o'rinbosar nomidan, "kimning o'rniga" bilan yoziladi.
/// </summary>
public class ApproverDelegation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DelegatorId { get; set; }
    public Identity Delegator { get; set; } = null!;
    public Guid DelegateId { get; set; }
    public Identity Delegate { get; set; } = null!;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public string? CreatedByName { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
}
