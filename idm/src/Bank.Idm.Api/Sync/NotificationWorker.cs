using Bank.Idm.Api.Security;
using Bank.Idm.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Sync;

/// <summary>Xabarnomalar: rejalashtirish va yuborish (faqat yetakchi nusxada — bir xabar ikki marta ketmasin).</summary>
public class NotificationWorker(IServiceScopeFactory scopes, IOptionsMonitor<IdmOptions> options, ILeadership cluster, TimeProvider time,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Notifications.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.CurrentValue.Notifications.IntervalSeconds)), time);
        do
        {
            if (!cluster.IsLeader) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var settings = options.CurrentValue.Notifications;
                var planned = await service.PlanAsync(settings, stoppingToken);
                var result = await service.DispatchAsync(settings, scope.ServiceProvider.GetRequiredService<INotificationSender>(), stoppingToken);
                if (planned > 0 || result.Failed > 0)
                    logger.LogInformation("Xabarnomalar: {Planned} ta yangi, {Sent} ta yuborildi, {Retrying} ta qayta uriniladi, {Failed} ta yuborilmadi",
                        planned, result.Sent, result.Retrying, result.Failed);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Xabarnomalar ishlovchisi xato bilan to'xtadi");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
