using Bank.Idm.Api.Security;
using Bank.Idm.Infrastructure.Access;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Sync;

/// <summary>O'tgan xodimlar: qaror muddati o'tgan rollarni tugatadi (faqat yetakchi nusxada). Guruhlarni tizimdan olish — provisioning.</summary>
public class MoverWorker(IServiceScopeFactory scopes, IOptionsMonitor<IdmOptions> options, ILeadership cluster, TimeProvider time,
    ILogger<MoverWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.CurrentValue.Mover.IntervalSeconds;
        if (interval <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(30, interval)), time);
        do
        {
            // Siyosat o'chirilgan bo'lsa ham, avval qo'yilgan muddatlar tugatiladi: aks holda rollar cheksiz "kutilmoqda" qolardi
            if (!cluster.IsLeader) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<MoverService>().ExpireAsync(ConnectorSync.SystemActor, "IdM o'tish", stoppingToken);
                if (expired > 0) logger.LogInformation("O'tish: qaror muddati o'tgan {Count} ta rol tugatildi", expired);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "O'tish (mover) ishlovchisi xato bilan to'xtadi");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
