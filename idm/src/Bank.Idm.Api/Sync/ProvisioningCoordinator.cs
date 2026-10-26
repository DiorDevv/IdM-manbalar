using Bank.Idm.Api.Security;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Provisioning;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Sync;

/// <summary>
/// Tizimga yozish aylanishi: har bir yozish yoqilgan tizim uchun (sinxronlash bilan to'qnashmasdan) reja tuziladi va navbatdagi
/// amallar bajariladi; oxirida tasdiqlangan arizalar "bajarildi" holatiga tekshiriladi.
/// </summary>
public class ProvisioningCoordinator(ConnectorSyncCoordinator sync, IServiceScopeFactory scopes, IOptionsMonitor<IdmOptions> options,
    ILogger<ProvisioningCoordinator> logger)
{
    public IReadOnlyDictionary<string, ConnectorDefinition> Enabled =>
        options.CurrentValue.Connectors.Where(c => c.Value.Enabled && c.Value.CanProvision).ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

    public ProvisioningMode ModeOf(string code) =>
        options.CurrentValue.Connectors.TryGetValue(code, out var d) && d.Enabled && d.CanProvision ? d.Provisioning.Mode : ProvisioningMode.Off;

    /// <summary>Faqat reja (dry-run): hech narsa bajarilmaydi. null — tizim topilmadi yoki yozish o'chiq.</summary>
    public async Task<PlanResult?> PlanAsync(string code, string actor, string? actorName, CancellationToken ct)
    {
        if (!Enabled.TryGetValue(code, out var definition)) return null;
        PlanResult? result = null;
        var ran = await sync.TryRunExclusiveAsync(code, async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdmDbContext>();
            var appId = await db.Applications.Where(a => a.Code == code).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            result = appId is null ? null
                : await scope.ServiceProvider.GetRequiredService<ProvisioningPlanner>().PlanAsync(appId.Value, definition.ToProvisioningSettings(), actor, actorName, ct);
        });
        return ran ? result : new PlanResult(0, 0, 0, false, $"{code} hozir sinxronlanmoqda, birozdan keyin qayta urining", []);
    }

    public async Task RunCycleAsync(CancellationToken ct)
    {
        foreach (var (code, definition) in Enabled)
        {
            try
            {
                await sync.TryRunExclusiveAsync(code, () => ProvisionAsync(code, definition, ct));
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                logger.LogError(e, "{Code}: tizimga yozish aylanishi xato bilan to'xtadi", code);
            }
        }
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var fulfilled = await scope.ServiceProvider.GetRequiredService<ProvisioningPlanner>()
                .CompleteRequestsAsync(ProvisioningExecutor.SystemActor, ProvisioningExecutor.SystemActorName, ct);
            if (fulfilled > 0) logger.LogInformation("{Count} ta ariza bajarildi", fulfilled);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogError(e, "Arizalar bajarilishini tekshirib bo'lmadi");
        }
    }

    private async Task ProvisionAsync(string code, ConnectorDefinition definition, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdmDbContext>();
        var appId = await db.Applications.Where(a => a.Code == code).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        if (appId is null) return;
        var settings = definition.ToProvisioningSettings();
        var plan = await scope.ServiceProvider.GetRequiredService<ProvisioningPlanner>()
            .PlanAsync(appId.Value, settings, ProvisioningExecutor.SystemActor, ProvisioningExecutor.SystemActorName, ct);
        if (plan.Created + plan.Obsoleted > 0)
            logger.LogInformation("{Code} reja: {Planned} amal, yangi {Created}, bekor {Obsolete}{Held}", code, plan.Planned, plan.Created, plan.Obsoleted,
                plan.Held ? " — chegaradan oshdi, tasdiq kutilmoqda" : "");
        if (plan.Held) logger.LogWarning("{Code}: {Count} ta yangi amal MaxAutoOperations ({Limit}) chegarasidan oshdi, administrator tasdig'i kerak",
            code, plan.Created, settings.MaxAutoOperations);

        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        if (!await db.ProvisioningOperations.AnyAsync(o => o.ApplicationId == appId && o.Status == ProvisioningStatus.Queued
                && (o.NextAttemptAt == null || o.NextAttemptAt <= now), ct)) return;
        using var connector = scope.ServiceProvider.GetRequiredService<IConnectorFactory>().CreateProvisioner(code, definition);
        var result = await scope.ServiceProvider.GetRequiredService<ProvisioningExecutor>().ExecuteDueAsync(appId.Value, connector, settings, ct: ct);
        var level = result.Failed + result.Retrying > 0 ? LogLevel.Warning : LogLevel.Information;
        logger.Log(level, "{Code} yozildi: bajarildi {Succeeded}, qayta urinadi {Retrying}, xato {Failed}", code, result.Succeeded, result.Retrying, result.Failed);
    }
}

/// <summary>Tizimga yozish: har ProvisioningIntervalSeconds'da reja va navbat (0 — fon ishlovchisi o'chiq).</summary>
public class ProvisioningWorker(ProvisioningCoordinator coordinator, IOptionsMonitor<IdmOptions> options, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.CurrentValue.ProvisioningIntervalSeconds;
        if (interval <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, interval)), time);
        do
        {
            await coordinator.RunCycleAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
