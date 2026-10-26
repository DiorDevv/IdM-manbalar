using Bank.Idm.Api.Security;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bank.Idm.Api.Sync;

/// <summary>Shu server nusxasi va uning yetakchiligi (fon ishlari faqat yetakchida).</summary>
public interface ILeadership
{
    string NodeId { get; }
    bool IsLeader { get; }
}

/// <summary>
/// Bir nechta server nusxasi (HA). Har nusxa <c>cluster_nodes</c>da yurak urishini yangilaydi. Yetakchi — PostgreSQL session advisory
/// lock egasi (alohida ulanishda): ulanish uzilsa yoki jarayon to'xtasa, lock o'zi bo'shaydi va boshqa nusxa yetakchi bo'ladi.
/// Yetakchi: jadval bo'yicha sinxronlash va tizimga yozishni bajaradi, yurak urishi to'xtagan nusxalarning "ishlayotgan"
/// sinxronlashlarini yopadi; yetakchi bo'lganda "bajarilmoqda"da qolgan yozish amallarini navbatga qaytaradi (amallar idempotent).
/// </summary>
public sealed class ClusterMembership(IServiceScopeFactory scopes, IConfiguration configuration, IOptionsMonitor<IdmOptions> options,
    TimeProvider time, ILogger<ClusterMembership> logger) : BackgroundService, ILeadership
{
    private const long LeaderLockKey = 0x69646d6c656164;   // "idmlead"
    private readonly DateTimeOffset _startedAt = time.GetUtcNow();
    private volatile NpgsqlConnection? _lock;

    public string NodeId { get; } = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}";
    public bool IsLeader => _lock is not null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.Cluster.HeartbeatSeconds)), time);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Klaster: yurak urishi yoki yetakchilik tekshiruvi xato bilan tugadi");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdmDbContext>();
        var now = time.GetUtcNow();
        var leader = IsLeader;
        if (await db.ClusterNodes.Where(n => n.Id == NodeId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.HeartbeatAt, now).SetProperty(n => n.IsLeader, leader), ct) == 0)
        {
            db.ClusterNodes.Add(new ClusterNode { Id = NodeId, Host = Environment.MachineName, StartedAt = _startedAt, HeartbeatAt = now, IsLeader = leader });
            await db.SaveChangesAsync(ct);
        }

        if (_lock is null) await TryAcquireAsync(db, ct);
        else await VerifyAsync(ct);
        if (!IsLeader) return;

        var dead = now - TimeSpan.FromSeconds(options.CurrentValue.Cluster.DeadAfterSeconds);
        // Yurak urishi to'xtagan nusxaning (yoki eski versiyaning, bajaruvchisi noma'lum) sinxronlashlari
        var closed = await db.ConnectorRuns
            .Where(r => r.Status == ConnectorRunStatus.Running && (r.RunnerId == null
                ? r.StartedAt < dead
                : !db.ClusterNodes.Any(n => n.Id == r.RunnerId && n.HeartbeatAt >= dead)))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ConnectorRunStatus.Failed).SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.Error, "Bajarayotgan server nusxasi to'xtadi, sinxronlash yakunlanmadi"), ct);
        if (closed > 0) logger.LogWarning("Klaster: to'xtagan nusxalarning {Count} ta sinxronlashi yopildi", closed);
        await db.ClusterNodes.Where(n => n.HeartbeatAt < now.AddDays(-7)).ExecuteDeleteAsync(ct);
    }

    private async Task TryAcquireAsync(IdmDbContext db, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(configuration.GetConnectionString("Idm"));
        try
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"select pg_try_advisory_lock({LeaderLockKey})", connection);
            if (await command.ExecuteScalarAsync(ct) is not true)
            {
                await connection.DisposeAsync();
                return;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        _lock = connection;
        await db.ClusterNodes.Where(n => n.Id != NodeId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsLeader, false), ct);
        await db.ClusterNodes.Where(n => n.Id == NodeId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsLeader, true), ct);
        // Avvalgi yetakchi bajarayotgan amallar: endi hech kim bajarmayapti
        var requeued = await ProvisioningService.RecoverInterruptedAsync(db, time, ct);
        logger.LogInformation("Klaster: {Node} yetakchi bo'ldi{Requeued}", NodeId, requeued > 0 ? $", {requeued} ta amal navbatga qaytarildi" : "");
    }

    private async Task VerifyAsync(CancellationToken ct)
    {
        try
        {
            await using var command = new NpgsqlCommand("select 1", _lock);
            await command.ExecuteScalarAsync(ct);
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(e, "Klaster: yetakchilik ulanishi uzildi, {Node} yetakchilikni yo'qotdi", NodeId);
            await ReleaseAsync();
        }
    }

    private async Task ReleaseAsync()
    {
        var connection = _lock;
        _lock = null;
        if (connection is not null) await connection.DisposeAsync();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await ReleaseAsync();
        try
        {
            // To'xtashda o'zini ro'yxatdan chiqaradi: tugallanmagan ishlarini keyingi yetakchi darhol yopadi
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IdmDbContext>().ClusterNodes.Where(n => n.Id == NodeId).ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception e) when (e is NpgsqlException or OperationCanceledException or InvalidOperationException)
        {
            logger.LogWarning(e, "Klaster: to'xtashda nusxani ro'yxatdan chiqarib bo'lmadi");
        }
    }
}

public class ClusterOptions
{
    public int HeartbeatSeconds { get; set; } = 10;
    /// <summary>Shuncha vaqt yurak urishi bo'lmagan nusxa to'xtagan hisoblanadi.</summary>
    public int DeadAfterSeconds { get; set; } = 45;
}
