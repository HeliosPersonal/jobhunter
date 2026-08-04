using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Profiles;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Renders the provider-agnostic <see cref="BatchRequestItem"/> list for a <em>matching</em> batch from the
/// jobs in a Run's scope, the active <see cref="Profile"/> and the active <see cref="CvVersion"/> (F4 SAD
/// §6.1, ADR-0006). The port lives in Domain so the Application submit handler depends on it rather than on
/// <c>JobHunter.Claude</c>; the implementation lives in the Claude adapter, where the versioned match prompt
/// and the generated tool-use schema live.
///
/// <para><strong>This is the one boundary the CV crosses.</strong> The active CV's
/// <see cref="CvVersion.ExtractedText"/> enters here — and only here — folded into each item's user content
/// by the match prompt. The implementation is the single place in the whole system that materialises CV
/// text into a string, which is what makes the leakage scan (T10) a bounded proof rather than a search of
/// the entire codebase. <see cref="BatchRequestItem.CustomId"/> is the job id verbatim, so a result maps
/// back with no lookup table.</para>
/// </summary>
public interface IMatchRequestBuilder
{
    /// <summary>
    /// Builds the request for <paramref name="jobs"/> against <paramref name="profile"/> and
    /// <paramref name="cvVersion"/>: one <see cref="BatchRequestItem"/> per job carrying the match system
    /// prompt, the rendered candidate-plus-role user content and the shared output schema, plus the prompt
    /// version stamped on every Match row (AC-11) and the pessimistic per-item output-token ceiling the cost
    /// estimate uses (invariant 6). A job with no enrichment is included with the enrichment lines omitted
    /// (AC-09), never skipped.
    /// </summary>
    MatchBatchRequest Build(
        IReadOnlyList<MatchJobContent> jobs,
        Profile profile,
        CvVersion cvVersion);
}

/// <summary>
/// A built matching batch ready to be estimated and submitted: the items, the prompt version stamped on
/// every row, and the pessimistic per-item output-token ceiling the estimate prices against. The deep-tier
/// analogue of <see cref="EnrichmentBatchRequest"/>.
/// </summary>
public sealed record MatchBatchRequest(
    string PromptVersion,
    IReadOnlyList<BatchRequestItem> Items,
    int MaxOutputTokensPerItem);
