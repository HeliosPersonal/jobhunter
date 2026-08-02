using System.Diagnostics.CodeAnalysis;
using JobHunter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobHunter.Worker.Cli;

/// <summary>
/// Applies outstanding EF Core migrations, then exits (S4/AC-11). Run as a pre-deploy Kubernetes Job so
/// three racing replicas never call <c>Migrate()</c> concurrently. Excluded from coverage — the
/// migrate path is exercised by the Testcontainers migration test, not by a unit test.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class MigrateCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("migrate");
        var context = scope.ServiceProvider.GetRequiredService<JobHunterDbContext>();

        var pending = (await context.Database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("No pending migrations; database is up to date.");
            return 0;
        }

        logger.LogInformation("Applying {PendingCount} pending migration(s).", pending.Count);
        await context.Database.MigrateAsync().ConfigureAwait(false);
        logger.LogInformation("Migrations applied.");
        return 0;
    }
}
