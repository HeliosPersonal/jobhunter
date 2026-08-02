using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

/// <summary>
/// Builds a <see cref="JobHunterDbContext"/> from a bare connection string, used by the test harness
/// and anywhere a context is needed outside the host's DI container. The configured options match the
/// runtime registration so a migration applied here behaves identically to one applied in production.
/// </summary>
public static class JobHunterDbContextFactory
{
    public static DbContextOptions<JobHunterDbContext> BuildOptions(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory")
                .MigrationsAssembly(typeof(JobHunterDbContext).Assembly.FullName))
            .Options;
    }

    public static JobHunterDbContext Create(string connectionString) =>
        new(BuildOptions(connectionString));
}
