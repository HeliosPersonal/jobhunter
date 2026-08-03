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
        // the client call and enforces the cost ceiling as a precondition (QG-2). Resolved by Wolverine
        // for EnrichmentSubmissionDue; its collaborators (scope query, request builder, cost accountant,
        // batch client) are registered by Infrastructure and Claude.
        services.AddScoped<Enrichment.EnrichmentSubmitHandler>();
        services.AddOptions<Enrichment.RunOptions>()
            .Validate(o => o.CeilingUsd > 0m, "Run:CeilingUsd must be positive.")
            .Validate(o => o.InitialLookBack > TimeSpan.Zero, "Run:InitialLookBack must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Enrichment.RunOptions>>().Value);

        // The batch poller (T11): a delayed job that re-enqueues itself on the backoff schedule, never a
        // loop (S5). Resolved by Wolverine for BatchPollDue; it polls the persisted provider batch and
        // never resubmits (AC-05), and ships partial at the deadline or the 6 h cap (AC-09).
        services.AddScoped<Enrichment.BatchPollHandler>();
        services.AddOptions<Enrichment.PollOptions>()
            .Validate(o => o.MaxPollDuration > TimeSpan.Zero, "Poll:MaxPollDuration must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Enrichment.PollOptions>>().Value);

        // The result-processing step (T12): streams the ended batch's results, parses each item
        // independently through the Domain port, upserts the valid enrichments (idempotent on
        // (job_id, run_id)), records the bad ones, writes the actual-cost ledger entry and advances the Run
        // to Matching (AC-06, AC-07, AC-10, QG-3). Resolved by Wolverine for BatchResultsReady; the parser
        // implementation is registered by Claude.
        services.AddScoped<Enrichment.BatchResultProcessingHandler>();

        return services;
    }
}
