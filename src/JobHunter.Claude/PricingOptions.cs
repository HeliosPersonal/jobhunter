using JobHunter.Domain.Pipeline;

namespace JobHunter.Claude;

/// <summary>
/// The pricing table, bound from configuration and validated at startup (contract §Cost model,
/// coding-standards §options). It is the single place the per-tier model id and USD-per-million-token
/// rates live: a model upgrade or a provider price change is a configuration change, not a code change
/// (ADR-0005). A stale table silently invalidates the ceiling, so the estimate-vs-actual metric watches
/// for drift (SAD §11 D3) — but a table that is missing a tier, or that points both tiers at one model,
/// fails startup rather than mispricing silently.
/// </summary>
public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    /// <summary>The per-tier rates, keyed by <see cref="ModelTier"/> name (<c>Cheap</c>, <c>Deep</c>).</summary>
    public IDictionary<string, TierPricing> Tiers { get; init; } = new Dictionary<string, TierPricing>();

    /// <summary>Resolves the rates for a tier, or throws if the tier is unpriced (startup validates this).</summary>
    public TierPricing For(ModelTier tier) =>
        Tiers.TryGetValue(tier.ToString(), out var pricing)
            ? pricing
            : throw new InvalidOperationException($"No pricing configured for tier '{tier}'.");
}

/// <summary>
/// One tier's model id and rates. Prices are USD per million tokens; the batch discount is a fraction in
/// <c>[0,1)</c> applied to both input and output (contract §Cost model). All money is <c>decimal</c>.
/// </summary>
public sealed class TierPricing
{
    /// <summary>The configured provider model id, e.g. <c>claude-haiku-4-5</c>.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>USD per million input tokens (list price, before the batch discount).</summary>
    public decimal InputPerMillion { get; init; }

    /// <summary>USD per million output tokens (list price, before the batch discount).</summary>
    public decimal OutputPerMillion { get; init; }

    /// <summary>The batch-API discount as a fraction, e.g. <c>0.5</c> for 50% off.</summary>
    public decimal BatchDiscount { get; init; }
}
