using Bank.Idm.Api.Security;
using Bank.Idm.Infrastructure.Reviews;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Sync;

/// <summary>Attestatsiya jadvali: muddati kelgan davr uchun kampaniya ochadi (faqat yetakchi nusxada; takror tekshiruv xavfsiz).</summary>
public class ReviewScheduleWorker(IServiceScopeFactory scopes, IOptionsMonitor<IdmOptions> options, ILeadership cluster, TimeProvider time,
    ILogger<ReviewScheduleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.CurrentValue.ReviewScheduleIntervalSeconds;
        if (interval <= 0) return;
        foreach (var (key, d) in options.CurrentValue.ReviewSchedules)
            if (ReviewScheduler.Validate(key, d) is { } error) logger.LogError("Attestatsiya jadvali e'tiborga olinmaydi: {Error}", error);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(30, interval)), time);
        do
        {
            if (!cluster.IsLeader || options.CurrentValue.ReviewSchedules.Count == 0) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var results = await scope.ServiceProvider.GetRequiredService<ReviewScheduler>().RunDueAsync(options.CurrentValue.ReviewSchedules, stoppingToken);
                foreach (var r in results)
                {
                    if (r.Outcome == "error") logger.LogError("Attestatsiya jadvali {Key} ({Period}): kampaniya ochilmadi — {Error}", r.Key, r.Period, r.Error);
                    else logger.LogInformation("Attestatsiya jadvali {Key} ({Period}): {Outcome}, {Items} ta yozuv", r.Key, r.Period, r.Outcome, r.Items);
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Attestatsiya jadvali ishlovchisi xato bilan to'xtadi");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
