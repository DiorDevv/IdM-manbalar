using Bank.Idm.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Idm.Tests;

public class AuditLogTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Chain_is_valid_and_links_each_event_to_the_previous_one()
    {
        await using var db = pg.CreateContext();
        var audit = new AuditLog(db, TimeProvider.System);
        await audit.SaveChangesAsync(new AuditEntry("tester", "Tester", "test.one", "x", "1", """{"a":1}"""), TestContext.Current.CancellationToken);
        await audit.SaveChangesAsync([new AuditEntry("tester", null, "test.two"), new AuditEntry("tester", null, "test.three")], TestContext.Current.CancellationToken);

        var events = await db.AuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.True(events.Count >= 3);
        for (var i = 1; i < events.Count; i++) Assert.Equal(events[i - 1].Hash, events[i].PreviousHash);
        Assert.True((await audit.VerifyChainAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Fact]
    public async Task Update_and_delete_are_rejected_by_the_database()
    {
        await using var db = pg.CreateContext();
        await new AuditLog(db, TimeProvider.System).SaveChangesAsync(new AuditEntry("tester", null, "test.immutable"), TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("update audit_events set action = 'hacked'", TestContext.Current.CancellationToken));
        Assert.Contains("append-only", update.MessageText);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("delete from audit_events", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("truncate audit_events", TestContext.Current.CancellationToken));
    }
}

/// <summary>Zanjirni ataylab buzadi: boshqa testlar ta'sirlanmasligi uchun o'z bazasida.</summary>
public class AuditTamperTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Tampering_that_bypasses_the_trigger_is_detected_by_the_hash_chain()
    {
        await using var db = pg.CreateContext();
        var audit = new AuditLog(db, TimeProvider.System);
        await audit.SaveChangesAsync([new AuditEntry("tester", null, "test.a"), new AuditEntry("tester", null, "test.b")], TestContext.Current.CancellationToken);
        var target = await db.AuditEvents.Where(e => e.Action == "test.a").Select(e => e.Id).FirstAsync(TestContext.Current.CancellationToken);

        // Superuser triggerni o'chirib yozuvni o'zgartiradi: buni zanjir tekshiruvi aniqlashi kerak
        await db.Database.ExecuteSqlRawAsync("alter table audit_events disable trigger user", TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlAsync($"update audit_events set actor_id = 'intruder' where id = {target}", TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlRawAsync("alter table audit_events enable trigger user", TestContext.Current.CancellationToken);

        var result = await audit.VerifyChainAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Equal(target, result.FirstBrokenId);
    }
}
