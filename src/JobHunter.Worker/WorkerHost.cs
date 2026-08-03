using System.Diagnostics.CodeAnalysis;
using JobHunter.Application;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Messaging;
using JobHunter.Infrastructure.Scheduling;
using JobHunter.Scrapers;
using JobHunter.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace JobHunter.Worker;

/// <summary>
/// Composes the long-running Worker host: platform defaults, Application + Infrastructure, the Wolverine
/// bus (durable outbox/inbox over RabbitMQ) and the Hangfire background server. This is the only host
/// that runs a Hangfire <em>server</em>, so recurring jobs have exactly one owner even if two Worker
/// replicas start (T09). Excluded from coverage — host composition, verified by the system starting.
/// </summary>
[ExcludeFromCodeCoverage]
public static class WorkerHost
{
    public static WebApplication CreateHost(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddEnvVariablesAndConfigureSecrets();
        builder.AddServiceDefaults();

        builder.Services.AddJobHunterApplication();
        builder.Services.AddJobHunterInfrastructure(builder.Configuration);

        // The Worker is the only host that fetches boards, so it composes the ATS adapter layer: the five
        // IJobSource adapters and the catalog the fetch handler dispatches through (dependency rule:
        // hosts -> Scrapers). Api and Telegram never fetch, so they never reference Scrapers.
        builder.Services.AddJobHunterScrapers();

        var messaging = builder.Configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>()
                        ?? new MessagingOptions();
        var hangfire = builder.Configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>()
                       ?? new HangfireOptions { EnableServer = true };
        var databaseConnection = builder.Configuration.GetConnectionString("JobHunter")
                                 ?? throw new InvalidOperationException("ConnectionStrings:JobHunter is required.");

        builder.UseWolverine(opts => WolverineConfiguration.Configure(opts, messaging, databaseConnection));
        builder.Services.AddJobHunterHangfire(hangfire, databaseConnection);

        var app = builder.Build();
        app.MapDefaultEndpoints();
        return app;
    }
}
