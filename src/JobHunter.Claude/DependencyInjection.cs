using System.Diagnostics.CodeAnalysis;
using JobHunter.Claude.Anthropic;
using JobHunter.Claude.Ollama;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Claude;

/// <summary>
/// The one composition method for the Claude adapter layer (coding-standards §DI). It binds and validates
/// <see cref="PricingOptions"/> at startup — an unpriced tier, or both tiers pointing at one model id,
/// fails the host rather than mispricing silently (ADR-F3-0002, SAD §11 D3) — and registers the
/// <see cref="ICostAccountant"/> and its token counter. Wiring is verified by the host starting, so this
/// type is excluded from coverage.
/// </summary>
[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddJobHunterClaude(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PricingOptions>()
            .Bind(configuration.GetSection(PricingOptions.SectionName))
            .Validate(HasEveryTier, "Pricing must configure every ModelTier (an unpriced tier makes the cost ceiling meaningless).")
            .Validate(HasPositiveRates, "Pricing rates must be positive and the batch discount in [0,1).")
            .Validate(TiersUseDistinctModels, "Cheap and Deep must not use the same model id (a silent cost doubling).")
            .ValidateOnStart();

        services.AddSingleton<ITokenCounter, HeuristicTokenCounter>();
        services.AddSingleton<ICostAccountant, CostAccountant>();

        // The enrichment request builder is the one place the versioned prompt and the generated schema
        // are turned into provider-agnostic items. It lives here (not in Application) so the CV never
        // enters and the Application submit handler depends on the Domain port, not on Anthropic (T10).
        services.AddSingleton<IEnrichmentRequestBuilder, Enrichment.EnrichmentRequestBuilder>();

        // The result parser is the other half of the same boundary: it turns a batch item's raw tool-use
        // JSON into a validated Enrichment aggregate through the tolerant eight-step parser, so the
        // Application result-processing handler (T12) depends on the Domain port, not on the parser here.
        services.AddSingleton<IEnrichmentResultParser, Enrichment.EnrichmentResultParser>();

        // The cheap-tier provider is a configuration switch, not a fork in the pipeline: the orchestrator,
        // the cost gate and the result-processing handler all submit through the same ILlmBatchClient port
        // (SAD S6, ADR-0005). Ollama on the cluster is the fallback whose absence degrades quality, not
        // availability — so it is only selected when explicitly configured.
        var provider = configuration.GetValue<string>("Llm:Provider");
        if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            AddOllama(services, configuration);
        }
        else
        {
            AddAnthropic(services, configuration);
        }

        return services;
    }

    private static void AddAnthropic(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Anthropic:ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Anthropic:BaseUrl is required.")
            .Validate(o => o.MaxOutputTokens > 0, "Anthropic:MaxOutputTokens must be positive.")
            .ValidateOnStart();

        // The batch client resolves a named HttpClient with the standard resilience handler: transient
        // transport faults and 5xx retry with backoff; a 4xx surfaces without retry (SAD §5). The API key
        // is set per-request in the adapter, never captured in the handler pipeline (invariant 12).
        services.AddHttpClient<ILlmBatchClient, AnthropicBatchClient>(AnthropicBatchClient.ClientName)
            .AddStandardResilienceHandler();
    }

    private static void AddOllama(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Ollama:BaseUrl is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Model), "Ollama:Model is required.")
            .Validate(o => o.MaxOutputTokens > 0, "Ollama:MaxOutputTokens must be positive.")
            .ValidateOnStart();

        // The synthesised batch lifecycle needs its result store to outlive a single adapter instance, so it
        // is a singleton (SAD §3). The chat client uses the same standard resilience handler as the Anthropic
        // tier; a per-item transport fault is caught in the adapter and recorded, never thrown (QG-3). The
        // adapter is composed from a named HttpClient by hand so its collaborators stay internal to the layer.
        services.AddSingleton<IOllamaResultStore, InMemoryOllamaResultStore>();
        services.AddHttpClient(OllamaBatchClient.ClientName).AddStandardResilienceHandler();
        services.AddSingleton<ILlmBatchClient>(sp => new OllamaBatchClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OllamaBatchClient.ClientName),
            sp.GetRequiredService<IOptions<OllamaOptions>>(),
            sp.GetRequiredService<IOllamaResultStore>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IIdGenerator>(),
            sp.GetRequiredService<ILogger<OllamaBatchClient>>()));
    }

    private static bool HasEveryTier(PricingOptions options) =>
        Enum.GetValues<ModelTier>().All(tier => options.Tiers.ContainsKey(tier.ToString()));

    private static bool HasPositiveRates(PricingOptions options) =>
        options.Tiers.Values.All(t =>
            t.InputPerMillion > 0m &&
            t.OutputPerMillion > 0m &&
            t.BatchDiscount is >= 0m and < 1m &&
            !string.IsNullOrWhiteSpace(t.ModelId));

    private static bool TiersUseDistinctModels(PricingOptions options)
    {
        var modelIds = options.Tiers.Values
            .Select(t => t.ModelId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        return modelIds.Count == modelIds.Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }
}
