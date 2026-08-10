using System.Diagnostics.CodeAnalysis;
using JobHunter.Application;
using JobHunter.Claude;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Messaging;
using JobHunter.Infrastructure.Scheduling;
using JobHunter.Scrapers;
using JobHunter.Search;
using JobHunter.ServiceDefaults;
using JobHunter.Telegram;
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

        // The Worker is the one host that runs the pipeline (Wolverine handlers, the Hangfire server), so it is
        // the only host that composes the Claude adapter layer — the enrichment/match/narrative request-builders
        // and result-parsers and the ILlmBatchClient the batch handlers submit through. The read-only Api and
        // Telegram hosts never touch Anthropic, so they never require its key (ADR-0005, coding-standards §DI).
        builder.Services.AddJobHunterClaude(builder.Configuration);

        // The digest narrative synthesiser is a pipeline collaborator of the (Wolverine-discovered)
        // DigestAssembler and depends on the Claude ports registered just above; like every other pipeline-only
        // service it is composed here, not in the shared Application registrations (F5 T05).
        builder.Services.AddScoped<JobHunter.Application.Reporting.INarrativeSynthesizer,
            JobHunter.Application.Reporting.NarrativeSynthesizer>();

        // The regret matcher (IRegretMatcher) is the same shape (F4 T21): it depends on the Claude match
        // request-builder/result-parser ports registered just above, so — like the synthesiser — it is composed
        // here in the pipeline host, never in the read-only Api or Telegram. Its two time bounds are
        // startup-validated so a stuck weekly batch can never hang the sampler's Hangfire job.
        builder.Services.AddOptions<JobHunter.Application.Ratings.RegretMatchingOptions>()
            .Bind(builder.Configuration.GetSection(JobHunter.Application.Ratings.RegretMatchingOptions.SectionName))
            .Validate(o => o.Timeout > TimeSpan.Zero, "RegretMatching:Timeout must be positive.")
            .Validate(o => o.PollInterval > TimeSpan.Zero, "RegretMatching:PollInterval must be positive.")
            .ValidateOnStart();
        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JobHunter.Application.Ratings.RegretMatchingOptions>>().Value);
        builder.Services.AddScoped<JobHunter.Domain.Abstractions.IRegretMatcher,
            JobHunter.Application.Ratings.RegretMatcher>();

        // The delivery handler is a Wolverine-discovered pipeline handler, so its one tunable — the Owner's
        // chat id, the chat_id half of the idempotence key — is registered and startup-validated here, in the
        // host that actually delivers, rather than in the shared Application registrations (F5 T08/T09).
        builder.Services.AddOptions<JobHunter.Application.Delivery.DeliveryOptions>()
            .Bind(builder.Configuration.GetSection(JobHunter.Application.Delivery.DeliveryOptions.SectionName))
            .Validate(o => o.OwnerChatId != 0, "Delivery:OwnerChatId must be set.")
            .ValidateOnStart();
        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JobHunter.Application.Delivery.DeliveryOptions>>().Value);

        // The scheduled send handlers — the 07:00 DeliveryHandler, the weekly WeeklyRatingHandler, the 08:00
        // ReminderSweepHandler and the F4 RegretSampler — are Wolverine handlers the Worker runs off its Hangfire
        // crons, and they send through INotifier and the three renderers. Those live in the shared Telegram
        // transport adapter, which both this host and the bus-less bot host compose; the Worker composes only the
        // outbound send path (never the bot's inbound command/callback wiring), so the token-bearing client, the
        // pacer and the renderers resolve here without pulling in the long-poll loop (Task #88).
        builder.Services.AddJobHunterTelegramTransport(builder.Configuration);

        // The Worker owns indexing: it runs the SearchIndexingHandler (writes a document per JobIndexRequested)
        // and the nightly reconcile/rebuild (F9-T02/T08), so it composes the Typesense adapter. The Api also
        // composes it, for the read side and the operator reindex endpoint.
        builder.Services.AddJobHunterSearch(builder.Configuration);

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
