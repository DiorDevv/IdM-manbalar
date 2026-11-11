using Bank.Idm.Api.Endpoints;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Access;

namespace Bank.Idm.Api.Sync;

/// <summary>Holat ko'rsatkichlarini har 15 daqiqada kunlik snapshot'ga yozadi (faqat yetakchi nusxada) — bosh sahifa trendlari uchun.</summary>
public class MetricSnapshotWorker(IServiceScopeFactory scopes, ILeadership cluster, TimeProvider time, ILogger<MetricSnapshotWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yetakchi aniqlanishiga va migratsiyalarga vaqt
        await Task.Delay(TimeSpan.FromSeconds(20), time, stoppingToken);
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            if (!cluster.IsLeader) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await InsightEndpoints.CaptureSnapshotAsync(scope.ServiceProvider.GetRequiredService<IdmDbContext>(),
                    scope.ServiceProvider.GetRequiredService<AccessAnalyzer>(), time, stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(e, "Ko'rsatkichlar snapshot'i yozilmadi");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
