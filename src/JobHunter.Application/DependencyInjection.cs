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
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Matching.PreMatchOptions>>().Value);

        return services;
    }
}
