using System.Collections.Concurrent;
using Bank.Idm.Api.Security;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Sync;

public enum SyncStartStatus { Started, NotConfigured, AlreadyRunning }

public record SyncStart(SyncStartStatus Status, Guid? RunId = null);

/// <summary>
/// Konnektorlarni ishga tushiradi: bitta tizim uchun bir vaqtda faqat bitta sinxronlash (jadval va qo'lda ishga tushirish
/// bir-biriga to'qnashmaydi). Qo'lda ishga tushirish fonda bajariladi, portal natijani ishga tushishlar tarixidan oladi.
/// </summary>
public class ConnectorSyncCoordinator(IServiceScopeFactory scopes, IOptionsMonitor<IdmOptions> options, IHostApplicationLifetime lifetime,
    ILogger<ConnectorSyncCoordinator> logger)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfigured(string code) => Definition(code) is not null;

    public bool IsRunning(string code) => _locks.TryGetValue(code, out var l) && l.CurrentCount == 0;

    /// <summary>Fonda ishga tushiradi va darhol qaytadi.</summary>
    public async Task<SyncStart> StartAsync(string code, ConnectorRunTrigger trigger, string actor, string? actorName, bool allowMassDelete = false)
    {
        var (status, runId, gate) = await BeginAsync(code, trigger, actor);
        if (status != SyncStartStatus.Started) return new SyncStart(status);
        _ = Task.Run(() => ExecuteAsync(code, runId!.Value, gate!, actorName, allowMassDelete, lifetime.ApplicationStopping));
        return new SyncStart(SyncStartStatus.Started, runId);
    }

    /// <summary>
    /// Tizimga yozish shu tizimning sinxronlashi bilan bir vaqtda bajarilmaydi (yozish eskirgan holatga asoslanmasligi va sinxronlash
    /// yarim yozilgan holatni o'qimasligi uchun). Band bo'lsa — false, keyingi aylanishda.
    /// </summary>
    public async Task<bool> TryRunExclusiveAsync(string code, Func<Task> action)
    {
        var gate = _locks.GetOrAdd(code, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0)) return false;
        try
        {
            await action();
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Jadval bo'yicha: tugashini kutadi.</summary>
    public async Task<ConnectorRun?> RunAsync(string code, ConnectorRunTrigger trigger, string actor, CancellationToken ct)
    {
        var (status, runId, gate) = await BeginAsync(code, trigger, actor);
        return status == SyncStartStatus.Started ? await ExecuteAsync(code, runId!.Value, gate!, null, false, ct) : null;
    }

    private async Task<(SyncStartStatus, Guid?, SemaphoreSlim?)> BeginAsync(string code, ConnectorRunTrigger trigger, string actor)
    {
        if (Definition(code) is null) return (SyncStartStatus.NotConfigured, null, null);
        var gate = _locks.GetOrAdd(code, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0)) return (SyncStartStatus.AlreadyRunning, null, null);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdmDbContext>();
            var app = await db.Applications.SingleOrDefaultAsync(a => a.Code == code);
            if (app is null) { gate.Release(); return (SyncStartStatus.NotConfigured, null, null); }
            var run = await scope.ServiceProvider.GetRequiredService<ConnectorSync>().StartRunAsync(app.Id, trigger, actor);
            return (SyncStartStatus.Started, run.Id, gate);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    private async Task<ConnectorRun?> ExecuteAsync(string code, Guid runId, SemaphoreSlim gate, string? actorName, bool allowMassDelete, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var definition = Definition(code)!;
            var factory = scope.ServiceProvider.GetRequiredService<IConnectorFactory>();
            var run = definition.IsHr
                ? await scope.ServiceProvider.GetRequiredService<HrSync>()
                    .ExecuteAsync(runId, factory.CreateHrSource(code, definition), definition.ToHrSettings(), actorName, allowMassDelete, ct)
                : await scope.ServiceProvider.GetRequiredService<ConnectorSync>()
                    .ExecuteAsync(runId, factory.Create(code, definition), definition.ToSettings(), actorName, allowMassDelete, ct);
            if (run.Status == ConnectorRunStatus.Succeeded)
            {
                // Xodim ma'lumoti (bo'lim, lavozim, holat) o'zgargan bo'lishi mumkin: avtomatik rollar darhol yangilanadi
                try
                {
                    var roles = await scope.ServiceProvider.GetRequiredService<BirthrightService>().RecalculateAsync(ConnectorSync.SystemActor, "IdM rollar", ct);
                    if (roles.Assigned + roles.Removed > 0)
                        logger.LogInformation("Avtomatik rollar: +{Assigned} / -{Removed}", roles.Assigned, roles.Removed);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "{Code} sinxronlashdan keyin avtomatik rollarni hisoblab bo'lmadi", code);
                }
            }
            var level = run.Status == ConnectorRunStatus.Succeeded ? LogLevel.Information : LogLevel.Warning;
            if (definition.IsHr)
                logger.Log(level, "{Code} sinxronlash: {Status}, o'qildi {Records} yozuv, yangi {Created}, o'zgargan {Updated}, ishdan ketgan {Terminated}, HR'da yo'q {Missing}. {Error}",
                    code, run.Status, run.AccountsRead, run.IdentitiesCreated, run.IdentitiesUpdated, run.IdentitiesTerminated, run.IdentitiesMissing, run.Error);
            else
                logger.Log(level, "{Code} sinxronlash: {Status}, o'qildi {Accounts} akkaunt / {Groups} guruh, yangi {Created}, o'chirildi {Deleted}. {Error}",
                    code, run.Status, run.AccountsRead, run.GroupsRead, run.AccountsCreated, run.AccountsDeleted, run.Error);
            return run;
        }
        catch (Exception e)
        {
            logger.LogError(e, "{Code} sinxronlash ({RunId}) kutilmagan xato bilan to'xtadi", code, runId);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private ConnectorDefinition? Definition(string code) =>
        options.CurrentValue.Connectors.FirstOrDefault(c => c.Key.Equals(code, StringComparison.OrdinalIgnoreCase) && c.Value.Enabled).Value;
}
