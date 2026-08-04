using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over the full content of the jobs a <em>matching</em> batch will assess (data-model
/// §jobs/§enrichments, F4 SAD §6.1). It is the matching analogue of <see cref="IEnrichmentScopeQuery"/>:
/// the submission step needs the posting text, the company facts <em>and</em> the latest enrichment for
/// each job so it can render — and therefore price — a match prompt per job. Read-only (Dapper), defined
/// in Domain so the Application submit handler depends on the port, not the Infrastructure query.
///
/// <para>The window is the Run's discovery window <c>[cutoffFrom, cutoffTo]</c>, unioned with the specific
/// carried-over job ids for their single retry (AC-08), exactly as enrichment scopes it — a job that made
/// it into enrichment must be matchable. The query returns only <c>Live</c> jobs, and the enrichment it
/// attaches is the latest one for the job (any Run), or <c>null</c> when the job has none, so a job is
/// never dropped for lacking an enrichment (AC-09). It selects <strong>nothing about the Owner</strong>:
/// the CV enters only at the Claude boundary (invariant — the CV crosses exactly one boundary, F4's).</para>
/// </summary>
public interface IMatchScopeQuery
{
    /// <summary>
    /// The full match content of every live job first seen in <c>[cutoffFrom, cutoffTo]</c>, plus the live
    /// jobs named by <paramref name="carriedOverJobIds"/>, each with its latest enrichment attached (or
    /// <c>null</c>). Deduplicated by job id, ordered deterministically so the estimate and the submission
    /// see the same items in the same order.
    /// </summary>
    Task<IReadOnlyList<MatchJobContent>> InScopeAsync(
        DateTimeOffset cutoffFrom,
        DateTimeOffset cutoffTo,
        IReadOnlyCollection<Guid> carriedOverJobIds,
        CancellationToken cancellationToken = default);
}
