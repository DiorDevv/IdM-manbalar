using Bank.Idm.Api.Security;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Sync;

/// <summary>
/// Jadval bo'yicha sinxronlash (faqat yetakchi nusxada): har daqiqada tekshiradi, oxirgi ishga tushishdan IntervalMinutes o'tgan bo'lsa
/// ishga tushiradi.
/// </summary>
public class ConnectorSyncWorker(ConnectorSyncCoordinator coordinator, IServiceScopeFactory scopes, IOptionsMonitor<IdmOptions> options,
    ILeadership cluster, TimeProvider time, ILogger<ConnectorSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        do
        {
            if (!cluster.IsLeader) continue;   // jadval faqat yetakchida (bir nechta nusxada takrorlanmasin)
            foreach (var (code, definition) in options.CurrentValue.Connectors.Where(c => c.Value.Enabled && c.Value.IntervalMinutes > 0))
            {
                try
                {
                    if (await IsDueAsync(code, definition.IntervalMinutes, stoppingToken))
                        await coordinator.RunAsync(code, ConnectorRunTrigger.Schedule, ConnectorSync.SystemActor, stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(e, "{Code}: jadval bo'yicha sinxronlashni boshlab bo'lmadi", code);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> IsDueAsync(string code, int intervalMinutes, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdmDbContext>();
        var last = await db.ConnectorRuns.Where(r => r.Application.Code == code).MaxAsync(r => (DateTimeOffset?)r.StartedAt, ct);
        return last is null || time.GetUtcNow() - last >= TimeSpan.FromMinutes(intervalMinutes);
    }
}
