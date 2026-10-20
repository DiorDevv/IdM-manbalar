using Bank.Idm.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Bank.Idm.Tests;

/// <summary>Har bir test sinfi uchun alohida, toza PostgreSQL (Docker). Audit triggeri va xesh zanjiri haqiqiy bazada sinaladi.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();
    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public IdmDbContext CreateContext() => new(new DbContextOptionsBuilder<IdmDbContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
