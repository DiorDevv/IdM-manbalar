using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Connectors;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Sync;

/// <summary>Har qanday manbani sinxronlashning umumiy qismi: ishga tushish yozuvi, xatolarni ushlash va yakuniy audit.</summary>
public abstract class SyncRunner(IdmDbContext db, AuditLog audit, TimeProvider time)
{
    protected IdmDbContext Db { get; } = db;
    protected AuditLog AuditTrail { get; } = audit;
    protected TimeProvider Time { get; } = time;

    public const string SystemActor = "system";
    public const string SystemActorName = "IdM sinxronlash";
    protected static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<ConnectorRun> StartRunAsync(Guid applicationId, ConnectorRunTrigger trigger, string triggeredBy, CancellationToken ct = default)
    {
        var run = new ConnectorRun { ApplicationId = applicationId, Trigger = trigger, TriggeredBy = triggeredBy, StartedAt = Time.GetUtcNow() };
        Db.ConnectorRuns.Add(run);
        await Db.SaveChangesAsync(ct);
        return run;
    }

    protected async Task<ConnectorRun> RunAsync(Guid runId, string? actorName, Func<ConnectorRun, Actor, Task<ConnectorRun>> apply, CancellationToken ct)
    {
        var run = await Db.ConnectorRuns.Include(r => r.Application).SingleAsync(r => r.Id == runId, ct);
        var actor = new Actor(run.TriggeredBy, run.TriggeredBy == SystemActor ? SystemActorName : actorName);
        try
        {
            return await apply(run, actor);
        }
        catch (Exception e) when (e is not OperationCanceledException || ct.IsCancellationRequested)
        {
            var message = e switch
            {
                OperationCanceledException => "Server to'xtatilgani uchun to'xtatildi",
                ConnectorException => e.Message,
                _ => $"{e.GetType().Name}: {e.Message}"
            };
            return await FinishAsync(runId, ConnectorRunStatus.Failed, message, actor, CancellationToken.None);
        }
    }

    protected async Task<bool> IsFirstLoadAsync(Guid applicationId, CancellationToken ct) =>
        !await Db.ConnectorRuns.AnyAsync(r => r.ApplicationId == applicationId && r.Status == ConnectorRunStatus.Succeeded, ct);

    /// <summary>Xato yoki xavfsizlik chegarasi: kontekstdagi barcha o'zgarishlar tashlanadi, faqat natija va audit yoziladi.</summary>
    protected async Task<ConnectorRun> FinishAsync(Guid runId, ConnectorRunStatus status, string error, Actor actor, CancellationToken ct,
        int recordsRead = 0, int groupsRead = 0)
    {
        Db.ChangeTracker.Clear();
        var run = await Db.ConnectorRuns.Include(r => r.Application).SingleAsync(r => r.Id == runId, ct);
        run.Status = status;
        run.Error = Truncate(error);
        run.AccountsRead = recordsRead;
        run.GroupsRead = groupsRead;
        run.FinishedAt = Time.GetUtcNow();
        await AuditTrail.SaveChangesAsync(actor.Entry(status == ConnectorRunStatus.Aborted ? "connector.sync-aborted" : "connector.sync-failed",
            "application", run.ApplicationId.ToString(), JsonSerializer.Serialize(new { application = run.Application.Code, runId, error = run.Error }, Json)), ct);
        return run;
    }

    protected static string Truncate(string value) => value.Length <= 4000 ? value : value[..3997] + "...";

    protected record Actor(string Id, string? Name)
    {
        public AuditEntry Entry(string action, string targetType, string targetId, string details) => new(Id, Name, action, targetType, targetId, details);
    }
}
