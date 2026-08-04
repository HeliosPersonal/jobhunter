using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Renders the single provider-agnostic <see cref="BatchRequestItem"/> for a digest's market-note synthesis
/// from a <see cref="NarrativeInput"/> (F5 SAD §6.1, T05, ADR-0006). The port lives in Domain so the
/// Application synthesiser depends on it rather than on <c>JobHunter.Claude</c>; the implementation lives in
/// the Claude adapter, where the versioned narrative prompt and its tool-use schema live — so the prompt
/// text, the schema and the prompt version are one place, and the Application layer stays free of any
/// provider concept (architecture rule 3).
///
/// <para>The rendering is pure and carries <strong>nothing about the Owner</strong>: a market note is
/// synthesised from the day's aggregate counts and one salary statistic — the numbers already destined for
/// the digest header and footer — so no CV text, no card reason and no job description ever enters this
/// boundary. The CV crosses exactly one boundary (F4's match prompt) and it is not this one.</para>
/// </summary>
public interface INarrativeRequestBuilder
{
    /// <summary>
    /// Builds the one-item request for <paramref name="input"/>: a single <see cref="BatchRequestItem"/>
    /// carrying the system prompt, the rendered user content and the market-note output schema, plus the
    /// prompt version stamped on the batch and the synthesised digest (AC on match parity) and the
    /// pessimistic per-item output-token ceiling the cost estimate prices against (invariant 6).
    /// </summary>
    NarrativeBatchRequest Build(NarrativeInput input);
}

/// <summary>
/// A built market-note batch ready to be estimated and submitted: the single item, the prompt version
/// stamped on the batch and the digest, and the pessimistic output-token ceiling the estimate prices
/// against (mirrors <see cref="EnrichmentBatchRequest"/>; a synthesis batch is always exactly one item).
/// </summary>
public sealed record NarrativeBatchRequest(
    string PromptVersion,
    IReadOnlyList<BatchRequestItem> Items,
    int MaxOutputTokensPerItem);
