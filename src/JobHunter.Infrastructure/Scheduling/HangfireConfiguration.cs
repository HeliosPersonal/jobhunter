using System.Diagnostics.CodeAnalysis;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// Wires Hangfire onto the single PostgreSQL under the <c>hangfire</c> schema (ADR-0004, T09). The
/// storage lives in every host so jobs can be enqueued anywhere, but only the Worker runs a background
/// <em>server</em> (<see cref="HangfireOptions.EnableServer"/>) — so two accidental Worker instances
/// still yield one recurring-job owner via Hangfire's distributed lock. Excluded from coverage: host
/// composition, exercised by the Worker's integration tests.
/// </summary>
[ExcludeFromCodeCoverage]
public static class HangfireConfiguration
{
    public static IServiceCollection AddJobHunterHangfire(
        this IServiceCollection services,
        HangfireOptions options,
        string databaseConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseConnectionString);

        services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                postgres => postgres.UseNpgsqlConnection(databaseConnectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = options.SchemaName,
                    PrepareSchemaIfNecessary = true,
                    UseSlidingInvisibilityTimeout = true,
                }));

        if (options.EnableServer)
        {
            services.AddHangfireServer(server => server.WorkerCount = options.WorkerCount);
        }

        return services;
    }
}
