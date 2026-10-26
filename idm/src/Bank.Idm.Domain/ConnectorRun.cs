namespace Bank.Idm.Domain;

/// <summary>Konnektorning bitta ishga tushishi: tizimdan o'qish va IdM bazasini u bilan solishtirish natijasi.</summary>
public class ConnectorRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public Application Application { get; set; } = null!;
    public ConnectorRunTrigger Trigger { get; set; }
    /// <summary>Qo'lda ishga tushirgan administrator logini (jadval bo'yicha bo'lsa "system").</summary>
    public string TriggeredBy { get; set; } = null!;
    /// <summary>Bajarayotgan server nusxasi (ClusterNode.Id): nusxa to'xtasa, yetakchi ishga tushishni yopadi.</summary>
    public string? RunnerId { get; set; }
    public ConnectorRunStatus Status { get; set; } = ConnectorRunStatus.Running;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public int AccountsRead { get; set; }
    public int GroupsRead { get; set; }
    public int AccountsCreated { get; set; }
    public int AccountsUpdated { get; set; }
    public int AccountsDisabled { get; set; }
    public int AccountsDeleted { get; set; }
    public int MembershipsAdded { get; set; }
    public int MembershipsRemoved { get; set; }
    public int IdentitiesCreated { get; set; }
    public int IdentitiesUpdated { get; set; }
    public int IdentitiesTerminated { get; set; }
    /// <summary>HR manbai: IdM'da faol, lekin HR faylida yo'q xodimlar.</summary>
    public int IdentitiesMissing { get; set; }
    /// <summary>Hech qaysi xodimga bog'lanmagan shaxsiy akkauntlar (ishga tushish oxiridagi holat).</summary>
    public int OrphanAccounts { get; set; }
    public string? Error { get; set; }
}

public enum ConnectorRunTrigger
{
    Schedule = 0,
    Manual = 1
}

public enum ConnectorRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    /// <summary>Xavfsizlik chegarasi: juda ko'p akkaunt yo'qolgan, o'zgarishlar qo'llanmadi.</summary>
    Aborted = 3
}
