using System.Diagnostics.CodeAnalysis;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
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
