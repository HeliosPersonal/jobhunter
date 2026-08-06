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
        services.AddScoped<IRunRepository, RunRepository>();
        services.AddScoped<IEnrichmentRepository, EnrichmentRepository>();
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<ICvVersionRepository, CvVersionRepository>();
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<IScoreRepository, ScoreRepository>();

        // F5 digest & delivery (T02): the digest aggregate goes through EF (assembled and persisted before
        // any send, SAD S2); the append-only delivery log is a raw ON CONFLICT DO NOTHING upsert whose unique
        // (run_id, chat_id, card_key) constraint is invariant 8 (ADR-F5-0002).
        services.AddScoped<IDigestRepository, DigestRepository>();
        services.AddScoped<IDeliveryLog, DeliveryLog>();
        // F5 digest assembly (T03): the read side the assembler draws its cards and suppression breakdown from
        // — every score in the Run joined to its current match's reasons and USD salary, ordered best-first.
        services.AddScoped<IDigestScopeQuery, DigestScopeQuery>();
        // F5 apply-link verification (T04): probes each card's apply destination through the shared
        // politeness-gated client (QG-2), so a confirmed-dead link drops its card without ever owning an
        // HttpClient of its own — robots, SSRF and the rate budget all apply to the probe (AC-11).
        services.AddScoped<IApplyLinkVerifier, ApplyLinkVerifier>();
        // F5 degraded-day assembly (T09): the count of the active company registry, snapshotted onto a
        // NothingNew digest so its "checked N companies, nothing new" reassurance states a real number
        // (AC-05). Read-only Dapper; registered in every host, harmless anywhere.
        services.AddScoped<IActiveCompanyCountQuery, ActiveCompanyCountQuery>();
        // F4 re-match backlog (T09): the durable seam between the bus-less Api activation write and the
        // Worker's next Run. Enqueue is idempotent per open job; the Run drains pending ids and consumes them.
        services.AddScoped<IReMatchBacklog, ReMatchBacklogRepository>();

        // F7 preference persistence (T02): signals are captured with a raw ON CONFLICT DO NOTHING upsert whose
        // unique (job_id, kind, occurred_at) constraint makes capture idempotent (F5/F6 write, F7 reads); the
        // preference model goes through EF as an immutable aggregate with its weights as owned children.
        services.AddScoped<ISignalRepository, SignalRepository>();
        services.AddScoped<IPreferenceModelRepository, PreferenceModelRepository>();

        // F4 CV upload (T03): the in-process, pure-managed text extractor behind the upload service. No
        // shell-out, no OCR — PdfPig reads embedded text, plain/Markdown is decoded as UTF-8.
        services.AddSingleton<ICvTextExtractor, Cv.CvTextExtractor>();
        services.AddScoped<IDegradedCoverageQuery, DegradedCoverageQuery>();
        services.AddScoped<IClosureSweepQuery, ClosureSweepQuery>();
        services.AddScoped<IRedetectionQuery, RedetectionQuery>();
        services.AddScoped<ILiveJobsQuery, LiveJobsQuery>();
        services.AddScoped<ICompanyJobsQuery, CompanyJobsQuery>();
        services.AddScoped<ILiveJobCounter, LiveJobCountQuery>();
        services.AddScoped<IJobProjectionSource, JobProjectionQuery>();
        services.AddScoped<IEnrichmentScopeQuery, EnrichmentScopeQuery>();
        services.AddScoped<IMatchScopeQuery, MatchScopeQuery>();
        services.AddScoped<IRankingScopeQuery, RankingScopeQuery>();
        services.AddScoped<ICurrentMatchQuery, CurrentMatchQuery>();
        services.AddScoped<IJobFactsSnapshotQuery, JobFactsSnapshotQuery>();
        services.AddScoped<ICardResolutionQuery, CardResolutionQuery>();
        // F5 /saved (T11): the roles the Owner saved — a Saved-kind signal joined back to the job, its company,
        // its latest score and its current match, newest-first and capped, so /saved renders the same card the
        // digest did (AC-12). Read-only Dapper; F5 reads the signals F5/F7 write.
        services.AddScoped<ISavedRolesQuery, SavedRolesQuery>();
        // F5 /stats (T11): this week's engagement — delivered from the append-only delivery_log, and the
        // opened/ignored/saved/applied reactions from signals — over a half-open window, so the command can
        // compare it against the week before. Read-only Dapper.
        services.AddScoped<IWeeklyStatsQuery, WeeklyStatsQuery>();
        // F5 digest rendering (T12): the display facts a card shows — the job's title, company, stage,
        // location, apply URL and published salary, plus its most recent enrichment estimate for the (est)
        // fallback — joined per job id at render time (the card snapshots only score and reasons). Read-only
        // Dapper; the production DigestRenderer both the 07:00 delivery and /digest depend on reads through it.
        services.AddScoped<ICardDisplayQuery, CardDisplayQuery>();
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

        // F9 operational endpoints (T07): the Api enqueues a full reindex or a history reprocess through this
        // port; Hangfire's PostgreSQL storage is composed in every host (ADR-0004) so the job is enqueued
        // from the Api and executed on the Worker's background server. Depends on IBackgroundJobClient, which
        // AddHangfire registers — present in the Api and Worker, the two hosts that compose Hangfire.
        services.AddScoped<IOperationScheduler, HangfireOperationScheduler>();

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

        // F5 daily digest schedule (T09): the three Europe/Kyiv ticks that bracket the day — 02:00 opens the
        // Run, 06:45 assembles whatever it produced, 07:00 delivers. Only the Worker's Hangfire server resolves
        // them, and each publishes one message onto the bus this host runs.
        services.AddScoped<DailyRunTrigger>();
        services.AddScoped<DigestAssemblyTrigger>();
        services.AddScoped<DigestDeliveryTrigger>();

        // The operator-requested rebuild and reprocess bodies (F9-T07): enqueued from the Api, executed here
        // on the Worker's Hangfire server. Registered alongside the recurring triggers so the server resolves
        // them; unlike the recurring bindings they carry no cron — they run on demand.
        services.AddScoped<IndexRebuildTrigger>();
        services.AddScoped<ReprocessTrigger>();

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

        // The day starts at 02:00 Kyiv (ADR-F5-0001): opening the Run five hours before delivery guarantees a
        // Run row exists by the 06:45 assembly deadline, so a degraded day still has something to assemble a
        // digest from. StartDailyRun is idempotent at the orchestrator, so a redelivered tick starts no second Run.
        services.AddSingleton(new RecurringJobBinding(
            DailyRunJobId,
            DailyRunCron,
            (cron, timeZone) => RecurringJob.AddOrUpdate<DailyRunTrigger>(
                DailyRunJobId,
                trigger => trigger.PublishAsync(),
                cron,
                new RecurringJobOptions { TimeZone = timeZone })));

        // The 06:45 assembly deadline (SAD §6.3): the digest is normally assembled early on RankingCompleted,
        // and this tick is the backstop that assembles whatever the day produced by the deadline — Partial for a
        // still-running Run, reduced for a CostAborted one — so the 07:00 slot always has a digest to deliver.
        services.AddSingleton(new RecurringJobBinding(
            DigestAssemblyJobId,
            DigestAssemblyCron,
            (cron, timeZone) => RecurringJob.AddOrUpdate<DigestAssemblyTrigger>(
                DigestAssemblyJobId,
                trigger => trigger.PublishAsync(),
                cron,
                new RecurringJobOptions { TimeZone = timeZone })));

        // The 07:00 delivery slot (QG-1): a hard commitment. The digest is assembled and held; this tick is the
        // only thing that releases it, so it lands on the slot and nothing lands before it. Delivery is
        // idempotent per card (invariant 8), so a redelivered tick re-sends nothing.
        services.AddSingleton(new RecurringJobBinding(
            DigestDeliveryJobId,
            DigestDeliveryCron,
            (cron, timeZone) => RecurringJob.AddOrUpdate<DigestDeliveryTrigger>(
                DigestDeliveryJobId,
                trigger => trigger.PublishAsync(),
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

    /// <summary>The daily run start at 02:00 Kyiv (F5 SAD §6.3, T09): opens the Run five hours before delivery.</summary>
    private const string DailyRunJobId = "daily-run";
    private const string DailyRunCron = "0 2 * * *";

    /// <summary>The digest assembly deadline at 06:45 Kyiv (F5 SAD §6.3, T09): the backstop before the slot.</summary>
    private const string DigestAssemblyJobId = "digest-assembly";
    private const string DigestAssemblyCron = "45 6 * * *";

    /// <summary>The digest delivery slot at 07:00 Kyiv (F5 SAD §6.3, QG-1, T09): the hard delivery commitment.</summary>
    private const string DigestDeliveryJobId = "digest-delivery";
    private const string DigestDeliveryCron = "0 7 * * *";

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
