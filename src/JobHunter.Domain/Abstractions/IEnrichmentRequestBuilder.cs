using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Renders the provider-agnostic <see cref="BatchRequestItem"/> list for an enrichment batch from the
/// jobs in a Run's scope (SAD §6.2, ADR-0006). The port lives in Domain so the Application submit handler
/// depends on it rather than on <c>JobHunter.Claude</c>; the implementation lives in the Claude adapter,
/// where the versioned prompt and the generated tool-use schema live — so the prompt text, the schema and
/// the prompt version are one place, and the Application layer stays free of any provider concept.
///
/// <para>The rendering is pure and carries <strong>nothing about the Owner</strong>: an enrichment prompt
/// describes the job, not the fit, so the CV never enters this boundary. <see cref="BatchRequestItem.CustomId"/>
/// is the job id verbatim, so a result maps back with no lookup table.</para>
/// </summary>
public interface IEnrichmentRequestBuilder
{
    /// <summary>
    /// Builds the request for <paramref name="jobs"/>: one <see cref="BatchRequestItem"/> per job carrying
    /// the system prompt, the rendered user content and the shared output schema, plus the prompt version
    /// stamped on every batch and enrichment row (AC-11) and the pessimistic per-item output-token ceiling
    /// the cost estimate uses (QG-2, ADR-F3-0002).
    /// </summary>
    EnrichmentBatchRequest Build(IReadOnlyList<EnrichmentJobContent> jobs);
}

/// <summary>
/// A built enrichment batch ready to be estimated and submitted: the items, the prompt version stamped on
/// every row, and the pessimistic per-item output-token ceiling the estimate prices against.
/// </summary>
public sealed record EnrichmentBatchRequest(
    string PromptVersion,
    IReadOnlyList<BatchRequestItem> Items,
    int MaxOutputTokensPerItem);
