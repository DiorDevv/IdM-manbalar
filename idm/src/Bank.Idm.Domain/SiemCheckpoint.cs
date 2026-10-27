namespace Bank.Idm.Domain;

/// <summary>SIEM'ga yuborilgan oxirgi audit yozuvi: server qayta ishga tushsa, yuborish shu joydan davom etadi (yo'qotishsiz).</summary>
public class SiemCheckpoint
{
    /// <summary>Qabul qiluvchi nomi (bir nechta SIEM bo'lishi mumkin).</summary>
    public string Name { get; set; } = null!;
    public long LastEventId { get; set; }
    public DateTimeOffset? LastSentAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
}
