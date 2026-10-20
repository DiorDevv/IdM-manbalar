using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Idm.Infrastructure;

/// <summary>dotnet ef migrations uchun: ulanish IDM_DB muhit o'zgaruvchisidan (yo'q bo'lsa, faqat model quriladi).</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<IdmDbContext>
{
    public IdmDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("IDM_DB") ?? "Host=localhost;Database=idm_design";
        return new IdmDbContext(new DbContextOptionsBuilder<IdmDbContext>().UseNpgsql(connection).Options);
    }
}
