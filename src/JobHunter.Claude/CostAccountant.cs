using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Options;

namespace JobHunter.Claude;

/// <summary>
/// Prices a batch before submission and after retrieval against the configured <see cref="PricingOptions"/>
/// (SAD §7, ADR-F3-0002). The estimate is what the cost ceiling is checked against — the orchestrator
/// never submits a batch it has not first priced (QG-2).
///
/// <para>Input tokens are counted from the <strong>actually rendered prompt</strong> via
/// <see cref="ITokenCounter"/>, not a per-job heuristic, which is why the estimate lands within 20% of the
/// reported actual. Output tokens are the schema's pessimistic per-item ceiling times the item count, so
/// the estimate errs toward over-stating spend. The batch discount is applied to both, so the returned
/// figure is the discounted price the ceiling is measured against — never the list price. All arithmetic
/// is <c>decimal</c>: a floating-point cost that drifts over a Run's entries would make the ceiling a
/// lie (coding-standards §5).</para>
/// </summary>
public sealed class CostAccountant(ITokenCounter tokenCounter, IOptions<PricingOptions> pricing) : ICostAccountant
{
    private const decimal Million = 1_000_000m;
    private readonly PricingOptions _pricing = pricing.Value;

    public CostEstimate Estimate(ModelTier tier, IReadOnlyList<string> renderedPrompts, int maxOutputTokensPerItem)
    {
        ArgumentNullException.ThrowIfNull(renderedPrompts);
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputTokensPerItem);

        var inputTokens = 0;
        foreach (var prompt in renderedPrompts)
        {
            inputTokens += tokenCounter.Count(prompt);
        }

        var outputTokens = renderedPrompts.Count * maxOutputTokensPerItem;
        return Price(tier, inputTokens, outputTokens);
    }

    public CostEstimate Actual(ModelTier tier, int inputTokens, int outputTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);
        return Price(tier, inputTokens, outputTokens);
    }

    private CostEstimate Price(ModelTier tier, int inputTokens, int outputTokens)
    {
        var rates = _pricing.For(tier);
        var multiplier = 1m - rates.BatchDiscount;

        var inputCost = inputTokens / Million * rates.InputPerMillion * multiplier;
        var outputCost = outputTokens / Million * rates.OutputPerMillion * multiplier;

        return new CostEstimate(inputCost + outputCost, inputTokens, outputTokens);
    }
}
