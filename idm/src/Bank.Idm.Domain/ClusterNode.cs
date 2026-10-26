namespace Bank.Idm.Domain;

/// <summary>
/// IdM API server nusxasi (HA: bir nechta nusxa bitta bazada). Har nusxa yurak urishini yangilab turadi; yetakchi (PostgreSQL advisory
/// lock egasi) fon ishlarini bajaradi va yurak urishi to'xtagan nusxalarning tugallanmagan ishlarini yopadi.
/// </summary>
public class ClusterNode
{
    /// <summary>Kompyuter nomi, jarayon raqami va tasodifiy qism: qayta ishga tushgan jarayon — yangi nusxa.</summary>
    public string Id { get; set; } = null!;
    public string Host { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset HeartbeatAt { get; set; }
    public bool IsLeader { get; set; }
}
