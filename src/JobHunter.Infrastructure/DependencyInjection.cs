using System.Diagnostics.CodeAnalysis;
using Hangfire;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Http;
using JobHunter.Infrastructure.Messaging;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Wolverine;

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
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IDegradedCoverageQuery, DegradedCoverageQuery>();
        services.AddScoped<IClosureSweepQuery, ClosureSweepQuery>();
        services.AddScoped<IRedetectionQuery, RedetectionQuery>();
        services.AddScoped<ILiveJobsQuery, LiveJobsQuery>();
        services.AddScoped<ICompanyJobsQuery, CompanyJobsQuery>();
        services.AddScoped<ILiveJobCounter, LiveJobCountQuery>();
        services.AddScoped<IJobProjectionSource, JobProjectionQuery>();
        services.AddScoped<IStaleJobsQuery, StaleJobsQuery>();
        services.AddScoped<IRawPostingReader, RawPostingReaderQuery>();
        services.AddScoped<IReprocessableJobsQuery, ReprocessableJobsQuery>();

        // F2 reprocessing and retention (T09): the offline recompute over stored payloads (zero network) and
        // the 90-day raw-payload prune. Both are resolved by the Worker's operator-scoped CLI verbs.
        services.AddScoped<JobHunter.Application.Reprocessing.ReprocessingService>();
        services.AddScoped<JobHunter.Application.Reprocessing.RetentionService>();

        // F2 technology tagging (T07): the committed vocabulary is loaded once from the embedded YAML — a
        // malformed file fails the host at startup, not at first tag — and the pure tagger over it is a
        // singleton the normalisation and deduplication handlers resolve.
        services.AddSingleton(_ => JobHunter.Infrastructure.Normalization.TechnologyVocabularyLoader.Load());
        services.AddSingleton<JobHunter.Application.Normalization.TechnologyTagger>();

        services.AddSingleton<RecurringJobRegistry>();

        AddDiscovery(services, configuration);
        AddPoliteHttp(services, configuration);

        return services;
    }

    /// <summary>
    /// Wires the six-hourly discovery cycle (SAD §6.1, T10): the due-source read model, the Hangfire job
    /// body, and the fan-out concurrency cap. The <see cref="SourceFetchRequested"/> listener is bounded to
    /// <c>Discovery:FetchConcurrency</c> by a Wolverine extension applied at bootstrap <em>after</em> F0's
    /// <c>WolverineConfiguration</c> — so the cap is added with no F0 messaging file modified.
    ///
    /// The schedule itself is registered through F0's <see cref="RecurringJobRegistry"/> seam by the
    /// <see cref="RecurringJobApplier"/>, gated on <see cref="HangfireOptions.EnableServer"/> so only the
    /// Worker (the single Hangfire-server host) declares and installs it — again with no F0 file modified.
    /// </summary>
    private static void AddDiscovery(IServiceCollection services, IConfiguration configuration)
    {
        // The due-source read model (Dapper). Registered in every host: the query is harmless anywhere.
        services.AddScoped<IDiscoveryCycleQuery, DiscoveryCycleQuery>();

        // Bound the fetch fan-out to the configured degree (SAD §8). Harmless where Wolverine is not run
        // (Api/Telegram): the extension is only resolved and applied when a bus is bootstrapped.
        services.AddWolverineExtension<FetchConcurrencyExtension>();

        var hangfire = configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>()
                       ?? new HangfireOptions();
        if (!hangfire.EnableServer)
        {
            // Only the Hangfire-server host installs the recurring schedule; other hosts have no Hangfire
            // storage, so declaring the job there would fault at start. The triggers depend on Wolverine's
            // IMessageBus, which only the Hangfire-server host (the Worker) runs — so they are registered
            // here too, keeping hosts without a bus (Api/Telegram) valid under container validation.
            return;
        }

        // The thin Hangfire job bodies; only ever resolved by the Worker's Hangfire server, where the bus runs.
        services.AddScoped<DiscoveryCycleTrigger>();
        services.AddScoped<ClosureSweepTrigger>();
        services.AddScoped<RedetectBindingTrigger>();
        services.AddScoped<JobLivenessCheckTrigger>();
        services.AddScoped<IndexReconcileTrigger>();

        services.AddSingleton(new RecurringJobBinding(
            DiscoveryCycleJobId,
            DiscoveryCycleCron,
            (cron, timeZone) => RecurringJob.AddOrUpdate<DiscoveryCycleTrigger>(
                DiscoveryCycleJobId,
                trigger => trigger.PublishAsync(),
                cron,
                new RecurringJobOptions { TimeZone = timeZone })));

        // The closure sweep runs just after each discovery cycle so a board that dropped a posting this cycle
        // is closed the same day (SAD §6.1, T13). Same cadence, offset by five minutes to follow the fetch.
        services.AddSingleton(new RecurringJobBinding(
            ClosureSweepJobId,
            ClosureSweepCron,
            (cron, timeZone) => RecurringJob.AddOrUpdate<ClosureSweepTrigger>(
                ClosureSweepJobId,
                trigger => trigger.PublishAsync(),
                cron,
                new RecurringJobOptions { TimeZone = timeZone })));

        // Binding re-detection runs daily (SAD §6.2, T09): each company is probed on the one day its stable
        // id-hash bucket matches, so the weekly re-probe is spread across the week rather than stampeding.
        services.AddSingleton(new RecurringJobBinding(
            RedetectBindingJobId,
            RedetectBindingCron,
            (cron, timeZone) => RecurringJob.AddOrUpdate<RedetectBindingTrigger>(
                RedetectBindingJobId,
                trigger => trigger.PublishAsync(),
                cron,
                new RecurringJobOptions { TimeZone = timeZone })));

        // The job-liveness check runs daily (SAD §6.2, T08): a canonical job whose every alias has gone stale
        // for two cycles is closed. Distinct from the six-hourly closure sweep, which closes a single posting
        // gone from its board; this closes a job gone from every board that carried it.
        services.AddSingleton(new RecurringJobBinding(
            JobLivenessCheckJobId,
            JobLivenessCheckCron,
            (cron, timeZone) => RecurringJob.AddOrUpdate<JobLivenessCheckTrigger>(
                JobLivenessCheckJobId,
                trigger => trigger.PublishAsync(),
                cron,
                new RecurringJobOptions { TimeZone = timeZone })));

        // The nightly index reconcile at 04:00 (SAD §6.3, T08): compare the live-job count against the
        // document count and re-index the live set when they diverge above the drift threshold. It runs
        // directly rather than publishing a message — reconcile is a self-contained maintenance operation,
        // not a pipeline stage — so the trigger resolves the Application service and awaits it.
        services.AddSingleton(new RecurringJobBinding(
            IndexReconcileJobId,
            IndexReconcileCron,
            (cron, timeZone) => RecurringJob.AddOrUpdate<IndexReconcileTrigger>(
                IndexReconcileJobId,
                trigger => trigger.RunAsync(),
                cron,
                new RecurringJobOptions { TimeZone = timeZone })));

        services.AddHostedService<RecurringJobApplier>();
    }

    /// <summary>The recurring-job id and cron (every six hours) for the discovery cycle (SAD §6.1).</summary>
    private const string DiscoveryCycleJobId = "discovery-cycle";
    private const string DiscoveryCycleCron = "0 */6 * * *";

    /// <summary>The closure sweep, five minutes past each six-hourly cycle (SAD §6.1, T13).</summary>
    private const string ClosureSweepJobId = "closure-sweep";
    private const string ClosureSweepCron = "5 */6 * * *";

    /// <summary>Binding re-detection, once a day at 03:30 (SAD §6.2, T09); day buckets spread the week.</summary>
    private const string RedetectBindingJobId = "redetect-bindings";
    private const string RedetectBindingCron = "30 3 * * *";

    /// <summary>The daily job-liveness check at 01:00 (SAD §6.2, T08): closes jobs stale across all sources.</summary>
    private const string JobLivenessCheckJobId = "job-liveness-check";
    private const string JobLivenessCheckCron = "0 1 * * *";

    /// <summary>The nightly search-index reconcile at 04:00 (SAD §6.3, F9-T08): re-indexes drift above 1%.</summary>
    private const string IndexReconcileJobId = "index-reconcile";
    private const string IndexReconcileCron = "0 4 * * *";

    /// <summary>
    /// Wires the shared outbound HTTP pipeline (SAD §8, QG-2): the politeness options, the SSRF guard,
    /// the robots policy, the per-host rate limiter (Redis when a cache is configured, in-memory
    /// otherwise) and the named <see cref="System.Net.Http.HttpClient"/> every ATS adapter is handed. The
    /// <see cref="PolitenessHandler"/> is attached to that client, so an adapter cannot construct a client
    /// that bypasses it.
    /// </summary>
    private static void AddPoliteHttp(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PolitenessOptions>()
            .Bind(configuration.GetSection(PolitenessOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.UserAgent), "Politeness:UserAgent is required.")
            .Validate(o => o.MaxResponseBytes > 0, "Politeness:MaxResponseBytes must be positive.")
            .ValidateOnStart();

        services.AddMemoryCache();

        services.AddSingleton<SsrfGuard>(_ => new SsrfGuard());

        var cache = configuration.GetConnectionString("Cache")
            ?? configuration[$"{ConnectionStringOptions.SectionName}:Cache"];

        if (!string.IsNullOrWhiteSpace(cache))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(cache));
            services.AddSingleton<IRateLimiter, RedisTokenBucket>();
        }
        else
        {
            services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();
        }

        // A bare client, free of the politeness handler, that only ever fetches robots.txt. It must not
        // recurse through the gated client (that would re-check robots to decide whether to fetch robots).
        services.AddHttpClient<HttpRobotsFetcher>();

        services.AddSingleton<IRobotsPolicy>(sp =>
        {
            var fetcher = sp.GetRequiredService<HttpRobotsFetcher>();
            return new RobotsPolicy(
                fetcher.FetchAsync,
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<IOptions<PolitenessOptions>>());
        });

        services.AddTransient<PolitenessHandler>();

        // The one gated client every ATS adapter resolves by name (SAD §8). Politeness is structural: the
        // handler sets the user-agent, checks SSRF and robots, spends the rate budget and caps the body.
        services.AddHttpClient(JobHunter.Application.Abstractions.PoliteHttp.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            })
            .AddHttpMessageHandler<PolitenessHandler>()
            .ConfigureHttpClient((sp, client) =>
                client.Timeout = sp.GetRequiredService<IOptions<PolitenessOptions>>().Value.RequestTimeout);
    }
}
