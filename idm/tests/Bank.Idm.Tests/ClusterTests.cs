using Bank.Idm.Api.Security;
using Bank.Idm.Api.Sync;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bank.Idm.Tests;

/// <summary>Bir nechta server nusxasi (HA) haqiqiy PostgreSQL'da: yetakchilik advisory lock bilan, to'xtagan nusxaning ishlari yopiladi.</summary>
public class ClusterTests : IAsyncLifetime
{
    private readonly PostgresFixture pg = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly MutableTime _time = new(new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero));
    private readonly List<ServiceProvider> _providers = [];
    private Guid _ad, _abs;

    public async ValueTask InitializeAsync()
    {
        await pg.InitializeAsync();
        await using var db = pg.CreateContext();
        var ad = new Application { Code = "AD", Name = "AD", Kind = ApplicationKind.Directory };
        var abs = new Application { Code = "ABS", Name = "ABS", Kind = ApplicationKind.CoreBanking };
        db.AddRange(ad, abs);
        await db.SaveChangesAsync(Ct);
        (_ad, _abs) = (ad.Id, abs.Id);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var p in _providers) await p.DisposeAsync();
        await pg.DisposeAsync();
    }

    private ClusterMembership Node()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Idm"] = pg.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddDbContext<IdmDbContext>(o => o.UseNpgsql(pg.ConnectionString));
        services.Configure<IdmOptions>(o => o.Cluster = new ClusterOptions { HeartbeatSeconds = 1, DeadAfterSeconds = 45 });
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return new ClusterMembership(provider.GetRequiredService<IServiceScopeFactory>(), configuration, provider.GetRequiredService<IOptionsMonitor<IdmOptions>>(),
            _time, NullLogger<ClusterMembership>.Instance);
    }

    [Fact]
    public async Task Exactly_one_node_leads_and_leadership_moves_when_it_stops()
    {
        var a = Node();
        var b = Node();
        await a.TickAsync(Ct);
        await b.TickAsync(Ct);

        Assert.True(a.IsLeader);
        Assert.False(b.IsLeader);
        await using (var db = pg.CreateContext())
            Assert.Equal([a.NodeId], await db.ClusterNodes.Where(n => n.IsLeader).Select(n => n.Id).ToListAsync(Ct));

        await a.StopAsync(Ct);
        await b.TickAsync(Ct);
        Assert.True(b.IsLeader);
        await using (var db = pg.CreateContext())
            Assert.Equal([b.NodeId], await db.ClusterNodes.Select(n => n.Id).ToListAsync(Ct));   // to'xtagan nusxa ro'yxatdan chiqdi
    }

    [Fact]
    public async Task Leader_whose_database_connection_drops_loses_leadership_and_another_node_takes_over()
    {
        var a = Node();
        var b = Node();
        await a.TickAsync(Ct);
        Assert.True(a.IsLeader);

        // Tarmoq uzilishi: yetakchining lock ulanishi server tomonidan yopiladi
        await using (var connection = new NpgsqlConnection(pg.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var kill = new NpgsqlCommand("select pg_terminate_backend(pid) from pg_locks where locktype = 'advisory' and pid <> pg_backend_pid() " +
                "and database = (select oid from pg_database where datname = current_database())", connection);
            await kill.ExecuteNonQueryAsync(Ct);
        }
        NpgsqlConnection.ClearAllPools();
        await b.TickAsync(Ct);   // lock bo'shadi
        Assert.True(b.IsLeader);
        await a.TickAsync(Ct);
        Assert.False(a.IsLeader);
    }

    [Fact]
    public async Task Leader_closes_runs_of_nodes_without_heartbeat_but_not_of_live_ones()
    {
        await using (var db = pg.CreateContext())
        {
            db.ClusterNodes.AddRange(
                new ClusterNode { Id = "dead-node", Host = "srv1", StartedAt = _time.Now.AddHours(-1), HeartbeatAt = _time.Now.AddMinutes(-5) },
                new ClusterNode { Id = "live-node", Host = "srv2", StartedAt = _time.Now.AddHours(-1), HeartbeatAt = _time.Now });
            db.ConnectorRuns.AddRange(
                new ConnectorRun { ApplicationId = _ad, TriggeredBy = "system", StartedAt = _time.Now.AddMinutes(-6), RunnerId = "dead-node" },
                new ConnectorRun { ApplicationId = _abs, TriggeredBy = "boss", StartedAt = _time.Now.AddMinutes(-1), RunnerId = "live-node" });
            db.ProvisioningOperations.Add(new ProvisioningOperation
            {
                ApplicationId = _ad, Kind = ProvisioningKind.DisableAccount, Status = ProvisioningStatus.Running, Reason = "leaver", DedupKey = "k1", CreatedAt = _time.Now
            });
            await db.SaveChangesAsync(Ct);
        }

        await Node().TickAsync(Ct);

        await using var check = pg.CreateContext();
        var runs = await check.ConnectorRuns.ToDictionaryAsync(r => r.RunnerId!, Ct);
        Assert.Equal(ConnectorRunStatus.Failed, runs["dead-node"].Status);
        Assert.Contains("to'xtadi", runs["dead-node"].Error);
        Assert.Equal(ConnectorRunStatus.Running, runs["live-node"].Status);
        // Yetakchi bo'lganda avvalgi yetakchining "bajarilmoqda" amallari navbatga qaytadi
        Assert.Equal(ProvisioningStatus.Queued, (await check.ProvisioningOperations.SingleAsync(Ct)).Status);
    }

    [Fact]
    public async Task Only_one_sync_per_system_can_run_across_all_nodes()
    {
        await using var db1 = pg.CreateContext();
        await using var db2 = pg.CreateContext();
        var first = await new ConnectorSync(db1, new AuditLog(db1, _time), _time).StartRunAsync(_ad, ConnectorRunTrigger.Manual, "boss", "node-1", Ct);
        var second = await new ConnectorSync(db2, new AuditLog(db2, _time), _time).StartRunAsync(_ad, ConnectorRunTrigger.Schedule, "system", "node-2", Ct);
        var other = await new ConnectorSync(db2, new AuditLog(db2, _time), _time).StartRunAsync(_abs, ConnectorRunTrigger.Schedule, "system", "node-2", Ct);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.NotNull(other);
    }
}
