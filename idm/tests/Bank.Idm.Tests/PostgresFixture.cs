using Bank.Idm.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Bank.Idm.Tests;

/// <summary>
/// Har bir test sinfi uchun alohida, toza PostgreSQL. Audit triggeri va xesh zanjiri haqiqiy bazada sinaladi.
/// Standart: Testcontainers (Docker). Docker bo'lmasa, IDM_TEST_POSTGRES (superuser ulanish qatori) ko'rsatilgan serverda
/// vaqtinchalik baza yaratiladi va test oxirida o'chiriladi.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private static readonly string? ExternalServer = Environment.GetEnvironmentVariable("IDM_TEST_POSTGRES");
    private readonly PostgreSqlContainer? _container = ExternalServer is null ? new PostgreSqlBuilder("postgres:16").Build() : null;
    private readonly string _database = $"idm_test_{Guid.NewGuid():N}";
    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        else
        {
            await ExecuteOnServerAsync($"create database {_database}");
            ConnectionString = new NpgsqlConnectionStringBuilder(ExternalServer) { Database = _database }.ConnectionString;
        }
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public IdmDbContext CreateContext() => new(new DbContextOptionsBuilder<IdmDbContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) { await _container.DisposeAsync(); return; }
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"drop database if exists {_database} with (force)");
    }

    private static async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ExternalServer);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
