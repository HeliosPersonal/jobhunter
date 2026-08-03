using System.Diagnostics.CodeAnalysis;
using JobHunter.Domain.Abstractions;
using JobHunter.Scrapers.Adapters;
using JobHunter.Scrapers.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Scrapers;

/// <summary>
/// The one composition method for the ATS adapter layer (coding-standards §3). Registers the five
/// <see cref="IJobSource"/> adapters and the <see cref="IJobSourceCatalog"/> that resolves one by
/// provider (QG-1) — so the discovery handler dispatches by <see cref="AtsKind"/> without ever
/// switching on a provider enum, and adding a provider is a new registration here, not a pipeline
/// change. The gated HTTP client the adapters share is registered by Infrastructure's
/// <c>AddPoliteHttp</c> (QG-2); this method only assumes it is present. Excluded from coverage — wiring
/// verified by the system starting.
/// </summary>
[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddJobHunterScrapers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The thin wrapper over the named politeness-gated client every adapter is handed (QG-2).
        services.AddSingleton<GatedHttpClient>();

        // One class per provider behind IJobSource; each is registered as the port so the catalog can
        // enumerate them. Adding a provider is one more line here — never a change to the pipeline.
        services.AddSingleton<IJobSource, GreenhouseJobSource>();
        services.AddSingleton<IJobSource, LeverJobSource>();
        services.AddSingleton<IJobSource, AshbyJobSource>();
        services.AddSingleton<IJobSource, WorkableJobSource>();
        services.AddSingleton<IJobSource, CareersPageJobSource>();

        services.AddSingleton<IJobSourceCatalog, JobSourceCatalog>();

        return services;
    }
}
