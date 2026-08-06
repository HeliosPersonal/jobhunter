using System.Diagnostics.CodeAnalysis;
using JobHunter.Application.Normalization;
using JobHunter.Application.Normalization.Providers;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Application;

/// <summary>
/// The one composition method for the Application layer. Registers the domain primitives every later
/// feature depends on. Wiring is verified by the system starting, so this type is excluded from coverage.
/// </summary>
[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddJobHunterApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, UuidV7Generator>();
        services.AddSingleton<IJitter, SystemJitter>();

        // Discovery application services — resolved by the CLI seed command and the recurring jobs.
        services.AddScoped<Discovery.CompanyRegistryService>();

        // DiscoveryOptions is passed to the cycle handler by Wolverine as a handler dependency; register
        // it as a resolvable singleton so both the handler and any consumer see one validated instance.
        services.AddOptions<Discovery.DiscoveryOptions>()
            .Validate(o => o.FetchConcurrency > 0, "Discovery:FetchConcurrency must be positive.")
            .Validate(o => o.RecentFetchWindow > TimeSpan.Zero, "Discovery:RecentFetchWindow must be positive.")
            .Validate(o => o.QuarantineFor > TimeSpan.Zero, "Discovery:QuarantineFor must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Discovery.DiscoveryOptions>>().Value);

        // F2 normalisation — one pure normaliser per ATS provider, indexed by the catalog so the handler
        // dispatches by AtsKind without switching on a provider enum (SAD §5). Adding a provider is a new
        // registration here, not a change to NormalizationHandler.
        // F9 search maintenance — the reconcile and rebuild coordinators, the in-process gate that
        // serialises them (a rebuild excludes a concurrent reconcile), and the reconcile options. Resolved
        // wherever an ISearchIndex is composed: the Api for the operator reindex endpoint (T07) and the
        // Worker for the nightly reconcile. The gate is a singleton so both contenders share one lock.
        services.AddSingleton<Search.IndexMaintenanceGate>();
        services.AddScoped<Search.IndexRebuildService>();
        services.AddScoped<Search.IndexReconcileService>();

        // F9 operational endpoints (T07): the corpus-stats snapshot the /api/admin/stats endpoint reads and
        // the source-unquarantine action /api/admin/sources/{id}/unquarantine drives. Both are pure
        // coordinators over Domain ports, resolved by the Api.
        services.AddScoped<Search.CorpusStatsService>();
        services.AddScoped<Search.SourceQuarantineService>();
        services.AddOptions<Search.ReconcileOptions>()
            .Validate(o => o.DriftThreshold is > 0 and < 1, "Search:Reconcile:DriftThreshold must be between 0 and 1.")
            .Validate(o => o.BatchSize > 0, "Search:Reconcile:BatchSize must be positive.")
            .Validate(o => o.RebuildBudget > TimeSpan.Zero, "Search:Reconcile:RebuildBudget must be positive.")
            .ValidateOnStart();

        services.AddSingleton<IPostingNormalizer, GreenhousePostingNormalizer>();
        services.AddSingleton<IPostingNormalizer, LeverPostingNormalizer>();
        services.AddSingleton<IPostingNormalizer, AshbyPostingNormalizer>();
        services.AddSingleton<IPostingNormalizer, WorkablePostingNormalizer>();
        services.AddSingleton<IPostingNormalizer, CareersPagePostingNormalizer>();
        services.AddSingleton<IPostingNormalizerCatalog, PostingNormalizerCatalog>();

        // F3 Run machinery — the orchestrator (start, scope, resume; T09) is resolved by the daily
        // Hangfire trigger and the startup resume sweep. RunOptions is passed to the orchestrator by
        // Wolverine as a handler dependency; register it as a resolvable singleton so the handler and any
        // consumer see one validated instance, and snapshot the ceiling onto each Run at creation.
        services.AddScoped<Enrichment.RunOrchestrator>();

        // The one spend-committing step (T10): builds the batch, prices it, ledgers the estimate before
        // the client call and enforces the cost ceiling as a precondition (QG-2). Discovered and
        // constructed by Wolverine for EnrichmentSubmissionDue (like every other handler — none is
        // eagerly registered here); its collaborators (scope query, request builder, cost accountant,
        // batch client) are composed only by the host that runs the pipeline, alongside AddJobHunterClaude.
        services.AddOptions<Enrichment.RunOptions>()
            .Validate(o => o.CeilingUsd > 0m, "Run:CeilingUsd must be positive.")
            .Validate(o => o.InitialLookBack > TimeSpan.Zero, "Run:InitialLookBack must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Enrichment.RunOptions>>().Value);

        // The batch poller (T11): a delayed job that re-enqueues itself on the backoff schedule, never a
        // loop (S5). Discovered by Wolverine for BatchPollDue; it polls the persisted provider batch and
        // never resubmits (AC-05), and ships partial at the deadline or the 6 h cap (AC-09).
        services.AddOptions<Enrichment.PollOptions>()
            .Validate(o => o.MaxPollDuration > TimeSpan.Zero, "Poll:MaxPollDuration must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Enrichment.PollOptions>>().Value);

        // The result-processing step (T12): streams the ended batch's results, parses each item
        // independently through the Domain port, upserts the valid enrichments (idempotent on
        // (job_id, run_id)), records the bad ones, writes the actual-cost ledger entry and advances the Run
        // to Matching (AC-06, AC-07, AC-10, QG-3). Discovered by Wolverine for BatchResultsReady; the parser
        // implementation is registered by Claude.

        // F4 CV upload (T03): the owner-scoped write path the Api endpoint drives — sniff, cap, extract,
        // hash, version and deactivate the previous active version, discarding the binary. Its collaborators
        // (the profile/CV repositories and the in-process text extractor) are registered by Infrastructure.
        services.AddScoped<Profiles.CvUploadService>();

        // F4 re-match on CV change (T09, ADR-F4-0002): the upload service runs this inline the moment a new
        // version is activated — the Api host has no message bus, so re-staling and re-match scheduling are a
        // synchronous owner-scoped write, not a published event. It re-stales older-version matches and queues
        // the recent live jobs for cheap-tier re-match into the backlog the next Run drains. The window is a
        // startup-validated option so the cost/coverage trade-off is tunable without a deploy.
        services.AddScoped<Profiles.ReMatchScheduler>();
        services.AddOptions<Profiles.ReMatchOptions>()
            .Validate(o => o.Window > TimeSpan.Zero, "ReMatch:Window must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Profiles.ReMatchOptions>>().Value);

        // F4 ranking (T08): the ranking tunables (top-count, salary-floor opt-in), passed to the handler by
        // Wolverine as a dependency, so register the validated value as a resolvable singleton. The learned
        // preference model is F7's; until it lands the null query answers "no active model" and ranking
        // renormalises the preference weight away, so the pipeline runs end-to-end today without F7's schema.
        services.AddOptions<Ranking.RankingOptions>()
            .Validate(o => o.TopJobCount > 0, "Ranking:TopJobCount must be positive.")
            .Validate(
                o => o.AntiGoalPenaltyFactor is >= 0m and <= 1m,
                "Ranking:AntiGoalPenaltyFactor must be in [0, 1].")
            .Validate(
                o => o.NegativeFamilyPenaltyFactor is >= 0m and <= 1m,
                "Ranking:NegativeFamilyPenaltyFactor must be in [0, 1].")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Ranking.RankingOptions>>().Value);
        services.AddSingleton<IPreferenceModelQuery, Ranking.NullPreferenceModelQuery>();

        // F4 pre-match filter (T12, ADR-F4-0003): the factual gate the matching submit handler applies before
        // the deep tier. Its tunables — the Owner's seniority and the two thresholds the PRD leaves as config —
        // are startup-validated and passed to the handler by Wolverine as a resolvable singleton. The bypass
        // (Run:MatchAllJobs) lives on RunOptions, already registered above.
        services.AddOptions<Matching.PreMatchOptions>()
            .Validate(o => o.SeniorityFloorGap > 0, "PreMatch:SeniorityFloorGap must be positive.")
            .Validate(
                o => o.SalaryConfidenceThreshold is >= 0m and <= 1m,
                "PreMatch:SalaryConfidenceThreshold must be in [0, 1].")
            .Validate(
                o => o.SeniorityFloorExemptStages is not null,
                "PreMatch:SeniorityFloorExemptStages must not be null.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Matching.PreMatchOptions>>().Value);

        // F5 digest assembly (T03): the assembler consumes RankingCompleted, selects and snapshots the cards,
        // builds the reconciling suppression breakdown and persists the digest before any send (S2). It is
        // discovered and constructed by Wolverine like every other handler; only its tunables — the card
        // threshold and cap — need registering, as a startup-validated resolvable singleton.
        services.AddOptions<Reporting.DigestOptions>()
            .Validate(
                o => o.CardScoreThreshold is >= 0m and <= 100m,
                "Digest:CardScoreThreshold must be in [0, 100].")
            .Validate(o => o.MaxCards > 0, "Digest:MaxCards must be positive.")
            .Validate(o => o.MinSalariesForAverage > 0, "Digest:MinSalariesForAverage must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Reporting.DigestOptions>>().Value);

        // F5 apply-link verification (T04): the assembler probes each selected card's apply destination and
        // drops a confirmed-unreachable one (AC-11). Its two bounds — the per-probe timeout and the fan-out
        // width — are startup-validated so an assembly pass can never wait unboundedly on a slow host.
        services.AddOptions<Reporting.ApplyVerificationOptions>()
            .Validate(o => o.Timeout > TimeSpan.Zero, "ApplyVerification:Timeout must be positive.")
            .Validate(o => o.MaxParallelism > 0, "ApplyVerification:MaxParallelism must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Reporting.ApplyVerificationOptions>>().Value);

        // F5 narrative synthesis (T05, ADR-F5-0001): the assembler asks the synthesiser for a market note; the
        // synthesiser makes one bounded deep-tier call, ceiling-checked and ledgered like every other batch,
        // and falls back to a deterministic template on a breach, an outage or an exhausted budget — so the
        // digest always ships. Its two time bounds are startup-validated so the call can never delay the digest.
        services.AddOptions<Reporting.NarrativeSynthesisOptions>()
            .Validate(o => o.Timeout > TimeSpan.Zero, "NarrativeSynthesis:Timeout must be positive.")
            .Validate(o => o.PollInterval > TimeSpan.Zero, "NarrativeSynthesis:PollInterval must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Reporting.NarrativeSynthesisOptions>>().Value);
        // The synthesiser itself (INarrativeSynthesizer) is a pipeline collaborator: it is a dependency of the
        // Wolverine-discovered DigestAssembler and it depends in turn on the Claude request-builder/result-parser
        // ports. Like the RunOrchestrator's batch collaborators above, it is composed only by the host that runs
        // the pipeline (the Worker), alongside AddJobHunterClaude — never in a read-only host such as the Api,
        // whose composition root must not require an Anthropic key it never uses.

        // F5 delivery (T08/T09, ADR-F5-0001/0002): the delivery handler is fired by the 07:00 slot
        // (DigestDeliveryDue), resolves the day's Run, renders its stored digest and sends each message exactly
        // once, writing a delivery-log row immediately after each send (S1). It is a Wolverine-discovered pipeline
        // handler, so its one tunable — DeliveryOptions.OwnerChatId, the chat_id half of the idempotence key — is
        // registered and startup-validated by the pipeline host, not here: a read-only host such as the Api must
        // not be forced to configure a chat id it never delivers to.

        // F5 card actions (T10, AC-08): the handler a card tap resolves to — it snapshots the job's facts at
        // the tap and captures one card-action signal in a single step. Unlike the pipeline handlers above it
        // is invoked directly (by the Telegram callback router), not discovered by Wolverine, so it is
        // registered here; its collaborators (the facts snapshot query and the signal repository) are
        // registered by Infrastructure.
        services.AddScoped<Actions.RecordCardActionHandler>();

        return services;
    }
}
