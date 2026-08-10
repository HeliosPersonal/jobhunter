using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the regret sampler (F4 T21, ADR-F4-0003): a sample of the jobs the pre-match filter
/// excluded from the latest Run's deep tier, returned as the same <see cref="MatchJobContent"/> a real match
/// would have been rendered from. Read-only (Dapper, architecture rule 4); defined in Domain so the
/// Application sampler depends on the port, not the SQL.
///
/// <para>A pre-match exclusion is, precisely, a <c>suppressed</c> score row of the latest Run that has
/// <strong>no <c>matches</c> row</strong> for that job and Run — the ADR's own definition: a factually
/// filtered job "never reaches the deep tier and never gets a match". That <em>NOT EXISTS</em> against
/// <c>matches</c> is what separates a pre-match exclusion from a post-ranking suppression (which is always
/// scored from a match), so the sampler measures the filter and only the filter.</para>
///
/// <para>The sample is scoped to the latest Run only, so a stale exclusion the current Run no longer makes
/// does not distort regret; ordered deterministically and capped at the caller's limit so a wide day never
/// produces an unbounded batch. It selects <strong>nothing about the Owner's CV</strong> — the CV crosses
/// exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public interface IFilterExcludedSampleQuery
{
    /// <summary>
    /// Up to <paramref name="limit"/> of the latest Run's pre-match-excluded jobs, each reconstructed as the
    /// <see cref="MatchJobContent"/> the deep tier would have judged (job facts plus the job's latest
    /// enrichment, or <c>null</c> when it has none). Empty when the latest Run excluded nothing, or there is
    /// no Run at all.
    /// </summary>
    Task<IReadOnlyList<MatchJobContent>> SampleAsync(int limit, CancellationToken cancellationToken = default);
}
