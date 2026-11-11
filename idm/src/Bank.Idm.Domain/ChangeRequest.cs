namespace Bank.Idm.Domain;

/// <summary>
/// Ikki kishilik tasdiq ("to'rt ko'z"): kirishni kengaytiradigan yoki nazoratni zaiflashtiradigan administrator amali darhol qo'llanmaydi —
/// so'rov sifatida saqlanadi va boshqa administrator tasdiqlaganda bajariladi. So'rov paytida amal xuddi shu kod bilan sinab ko'riladi
/// (tranzaksiya bekor qilinadi): <see cref="Preview"/> — tasdiqlovchi ko'radigan audit yozuvlari (aynan nima o'zgaradi).
/// </summary>
public class ChangeRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Amal turi, masalan "role.rule-add" (<c>ChangeKinds</c>).</summary>
    public string Kind { get; set; } = null!;
    /// <summary>Amal parametrlari (JSON): tasdiqlanganda shu bilan bajariladi.</summary>
    public string Payload { get; set; } = null!;
    /// <summary>Sinov bajarilishidagi audit yozuvlari (JSON massiv: action, details).</summary>
    public string Preview { get; set; } = "[]";
    public ChangeRequestStatus Status { get; set; } = ChangeRequestStatus.Pending;
    public string RequestedBy { get; set; } = null!;
    public string? RequestedByName { get; set; }
    public string? RequestedIp { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
    /// <summary>Tasdiqlanganda bajarib bo'lmadi (masalan shu orada ruxsat o'chirilgan) — sababi.</summary>
    public string? Error { get; set; }
}

public enum ChangeRequestStatus
{
    Pending = 0,
    Applied = 1,
    Rejected = 2,
    Cancelled = 3,
    /// <summary>Tasdiqlandi, lekin bajarib bo'lmadi: hech narsa o'zgarmadi.</summary>
    Failed = 4
}
