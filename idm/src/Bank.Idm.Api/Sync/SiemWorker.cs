using Bank.Idm.Api.Security;
using Bank.Idm.Infrastructure.Siem;
using Microsoft.Extensions.Options;

namespace Bank.Idm.Api.Sync;

/// <summary>Audit jurnalini SIEM'ga uzluksiz yuboradi (faqat yetakchi nusxada — takror yuborilmasin). Xatoda — keyingi aylanishda shu joydan.</summary>
public class SiemWorker(IServiceScopeFactory scopes, IOptionsMonitor<IdmOptions> options, ILeadership cluster, TimeProvider time,
    ILogger<SiemWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Siem.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.Siem.IntervalSeconds)), time);
        var failing = false;
        do
        {
            if (!cluster.IsLeader) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var forwarder = scope.ServiceProvider.GetRequiredService<SiemForwarder>();
                ForwardResult result;
                // Ko'p yozuv to'plangan bo'lsa, bir aylanishda bir nechta to'plam
                do result = await forwarder.ForwardAsync(options.CurrentValue.Siem, stoppingToken);
                while (result.Sent == options.CurrentValue.Siem.BatchSize && !stoppingToken.IsCancellationRequested);
                if (failing) logger.LogInformation("SIEM'ga yuborish tiklandi (oxirgi yozuv #{Id})", result.LastEventId);
                failing = false;
            }
            catch (IOException e)
            {
                if (!failing) logger.LogError(e, "SIEM'ga audit yuborilmayapti: {Error}", e.Message);   // har aylanishda takrorlanmasin
                failing = true;
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "SIEM ishlovchisi kutilmagan xato bilan to'xtadi");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
