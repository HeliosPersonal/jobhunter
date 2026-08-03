using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over the full content of the jobs an enrichment batch will assess (data-model §jobs).
/// The submission step (T10) needs the posting text and company facts to render — and therefore price —
/// a prompt per job, which <see cref="ILiveJobsQuery"/> deliberately does not carry. Read-only (Dapper),
/// defined in Domain so the Application submit handler depends on the port, not the Infrastructure query.
///
/// <para>The window is the Run's discovery window <c>[cutoffFrom, cutoffTo]</c>, and the specific job ids
/// carried over from the previous Run's failed items are re-included for their single retry (AC-08). The
/// query returns only <c>Live</c> jobs — a closed or quarantined job is never enriched.</para>
/// </summary>
public interface IEnrichmentScopeQuery
{
    /// <summary>
    /// The full enrichment content of every live job first seen in <c>[cutoffFrom, cutoffTo]</c>, plus the
    /// live jobs named by <paramref name="carriedOverJobIds"/> (the previous Run's failed items retrying
    /// once, AC-08) regardless of when they were first seen. Deduplicated by job id, ordered
    /// deterministically so the estimate and the submission see the same items in the same order.
    /// </summary>
    Task<IReadOnlyList<EnrichmentJobContent>> InScopeAsync(
        DateTimeOffset cutoffFrom,
        DateTimeOffset cutoffTo,
        IReadOnlyCollection<Guid> carriedOverJobIds,
        CancellationToken cancellationToken = default);
}
