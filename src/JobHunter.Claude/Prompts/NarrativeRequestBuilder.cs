using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// Renders the one-item market-note batch request from a <see cref="NarrativeInput"/> (F5 SAD §6.1, T05).
/// It is the Claude-side implementation of <see cref="INarrativeRequestBuilder"/>: the Application
/// synthesiser holds the cost gate and the persistence, and delegates the one thing that must know the
/// prompt and the schema — building the item — to here, so the versioned prompt
/// (<see cref="DigestNarrativePrompt"/>), the generated schema (<see cref="DigestNarrativeSchema"/>) and the
/// prompt version stay in one place and the Application layer stays free of any Anthropic concept
/// (architecture rule 3).
///
/// <para>Like enrichment, and unlike matching, there is <strong>no cache prefix</strong>: a synthesis batch
/// is a single item with no shared per-Owner head to cache. The rendering carries nothing about the Owner
/// (<see cref="DigestNarrativePrompt.Render"/> reads only aggregate counts), so the CV never enters this
/// boundary.</para>
/// </summary>
public sealed class NarrativeRequestBuilder : INarrativeRequestBuilder
{
    /// <summary>
    /// The pessimistic per-item output-token ceiling the estimate prices against: a market note is two or
    /// three sentences, but the ceiling is deliberately over-stated so the cost check errs toward
    /// under-spending (ADR-F3-0002, invariant 6).
    /// </summary>
    public const int MaxOutputTokensPerItem = 400;

    /// <summary>The single item's custom id — a synthesis batch has exactly one item, so a fixed id maps it back.</summary>
    public const string ItemCustomId = "digest-narrative";

    public NarrativeBatchRequest Build(NarrativeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var item = new BatchRequestItem(
            CustomId: ItemCustomId,
            SystemPrompt: DigestNarrativePrompt.System,
            UserContent: DigestNarrativePrompt.Render(input),
            OutputSchema: DigestNarrativeSchema.Build());

        return new NarrativeBatchRequest(DigestNarrativePrompt.PromptVersion, [item], MaxOutputTokensPerItem);
    }
}
