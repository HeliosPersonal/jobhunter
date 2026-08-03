using JobHunter.Domain.Pipeline;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Prices a batch before it is submitted and after it returns (SAD §7, ADR-F3-0002). The estimate is
/// what the cost ceiling is checked against, which is what makes the ceiling a precondition rather than
/// an alarm (QG-2): the orchestrator asks for an estimate, compares it to the remaining ceiling, and only
/// then decides whether the LLM client is called at all.
///
/// <para>Estimation counts tokens from the <strong>actually rendered prompt</strong>, not a per-job
/// heuristic — that is why the estimate lands within 20% of the reported actual rather than within a
/// factor. Output tokens are estimated pessimistically from the schema's maximum plausible size, so the
/// ceiling errs toward under-spending.</para>
/// </summary>
public interface ICostAccountant
{
    /// <summary>
    /// Estimates the discounted cost of one batch on <paramref name="tier"/>, given the fully rendered
    /// prompt text of each item and a pessimistic per-item output-token ceiling derived from the schema's
    /// maximum plausible size. Input tokens are counted from the real prompt text; output tokens are
    /// <paramref name="maxOutputTokensPerItem"/> × item count, so the estimate errs toward under-spending.
    /// The batch discount is already applied to the returned figure.
    /// </summary>
    CostEstimate Estimate(ModelTier tier, IReadOnlyList<string> renderedPrompts, int maxOutputTokensPerItem);

    /// <summary>
    /// Prices the provider's reported token usage for a completed batch on <paramref name="tier"/> — the
    /// <see cref="LedgerEntryKind.Actual"/> figure. The batch discount is already applied.
    /// </summary>
    CostEstimate Actual(ModelTier tier, int inputTokens, int outputTokens);
}
