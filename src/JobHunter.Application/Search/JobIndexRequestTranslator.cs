using JobHunter.Contracts.Pipeline;
using Wolverine;

namespace JobHunter.Application.Search;

/// <summary>
/// Translates the job-lifecycle events that should change the search index into the one
/// <see cref="JobIndexRequested"/> the indexer consumes (SAD §6.1, F9-T02). Keeping the translation here
/// means <see cref="SearchIndexingHandler"/> depends on a single message shape, and adding a new
/// re-index trigger (a ranking or an application-status change once F4/F6 land) is a new method here
/// rather than a change to the indexer.
///
/// <para>Each translation is a pure map with no state, so a redelivered lifecycle event produces the same
/// <see cref="JobIndexRequested"/> and the downstream inbox and the id-keyed upsert collapse the duplicate
/// (invariant 8). <see cref="OccurredAt"/> is carried through from the source event — a fixed instant, not
/// "now" — so a replay is byte-identical.</para>
/// </summary>
public static class JobIndexRequestTranslator
{
    /// <summary>A newly-canonical job (F2) needs its first document.</summary>
    public static JobIndexRequested Handle(JobDiscovered discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        return new JobIndexRequested(discovered.JobId, JobIndexRequested.Upsert, discovered.OccurredAt);
    }

    /// <summary>A closed job (F1/F2 lifecycle) has its document removed.</summary>
    public static JobIndexRequested Handle(JobClosed closed)
    {
        ArgumentNullException.ThrowIfNull(closed);
        return new JobIndexRequested(closed.JobId, JobIndexRequested.Delete, closed.OccurredAt);
    }
}
