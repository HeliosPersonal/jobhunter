using System.Diagnostics.CodeAnalysis;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Messaging;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure;

/// <summary>
/// The one composition method for the Infrastructure layer (coding-standards §3). Registers the write
/// context, the Dapper connection factory, the reference repository/query and the scheduling registry,
/// and binds+validates every options class at startup via <c>.Validate().ValidateOnStart()</c>.
/// Excluded from coverage — wiring is verified by the system starting.
/// </summary>
[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddJobHunterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ConnectionStringOptions>()
            .Bind(configuration.GetSection(ConnectionStringOptions.SectionName))
            .Validate(o => o.IsValid(out _), "Connection strings are invalid or incomplete.")
            .ValidateOnStart();

        services.AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<HangfireOptions>()
            .Bind(configuration.GetSection(HangfireOptions.SectionName))
            .ValidateOnStart();

        var connectionString =
            configuration.GetConnectionString("JobHunter")
            ?? configuration[$"{ConnectionStringOptions.SectionName}:JobHunter"]
            ?? throw new InvalidOperationException(
                "ConnectionStrings:JobHunter is required. The host refuses to start without it (AC-09).");

        services.AddDbContext<JobHunterDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(JobHunterDbContext).Assembly.FullName)));

        services.AddSingleton<INpgsqlConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));

        services.AddScoped<IPlatformMarkerRepository, PlatformMarkerRepository>();
        services.AddScoped<PlatformMarkerQuery>();

        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IJobSourceRepository, JobSourceRepository>();
        services.AddScoped<IRawPostingRepository, RawPostingRepository>();

        services.AddSingleton<RecurringJobRegistry>();

        return services;
    }
}
