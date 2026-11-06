using Bank.Idm.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Bank.Idm.Tests;

/// <summary>
/// Har bir test sinfi uchun alohida, toza PostgreSQL. Audit triggeri va xesh zanjiri haqiqiy bazada sinaladi.
/// Standart: Testcontainers (Docker). Docker bo'lmasa, IDM_TEST_POSTGRES (superuser ulanish qatori) ko'rsatilgan serverda
/// vaqtinchalik baza yaratiladi va test oxirida o'chiriladi.
/// <paramref name="keepAs"/> berilsa (faqat IDM_TEST_POSTGRES bilan): shu nomli baza qayta yaratiladi va test oxirida qoldiriladi —
/// masalan yuklama testi ma'lumotlari ustida API'ni k6 bilan sinash uchun (tests/load/README.md).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private static readonly string? ExternalServer = Environment.GetEnvironmentVariable("IDM_TEST_POSTGRES");
    private readonly PostgreSqlContainer? _container;
    private readonly string _database;
    private readonly bool _keep;

    public PostgresFixture() : this(null) { }

    internal PostgresFixture(string? keepAs)   // xUnit sinf fixture uchun faqat bitta public konstruktor
    {
        if (keepAs is not null && (ExternalServer is null || !System.Text.RegularExpressions.Regex.IsMatch(keepAs, "^[a-z_][a-z0-9_]{0,62}$")))
            throw new InvalidOperationException("Bazani saqlash faqat IDM_TEST_POSTGRES bilan va nom [a-z0-9_] bo'lishi kerak");
        _container = ExternalServer is null ? new PostgreSqlBuilder("postgres:16").Build() : null;
        _database = keepAs ?? $"idm_test_{Guid.NewGuid():N}";
        _keep = keepAs is not null;
    }
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
            if (_keep) await ExecuteOnServerAsync($"drop database if exists {_database} with (force)");
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
        if (_keep) return;
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
