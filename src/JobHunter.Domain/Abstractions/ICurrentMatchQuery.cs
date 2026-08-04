namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the pre-match filter's lifecycle rule (ADR-F4-0003, T12): which of a set of jobs
/// already carry a <em>current</em> match against a given CV version. A job that does would only be re-judged
/// to reach a conclusion already reached, so the filter excludes it from the deep tier rather than repay for it.
///
/// <para>It exists so the pure <see cref="Application"/>-layer filter never references the <c>matches</c> table
/// itself — the submission handler resolves the fact through this port and hands the filter a plain boolean, which
/// is what the architecture test that forbids the filter from touching <c>matches</c>, <c>scores</c> or CV text
/// relies on. Read-only (Dapper); a stale-marked match (its CV version superseded) is deliberately not counted,
/// so a CV change re-opens every job for matching (AC-08).</para>
/// </summary>
public interface ICurrentMatchQuery
{
    /// <summary>
    /// The subset of <paramref name="jobIds"/> that already have a current match against
    /// <paramref name="cvVersionId"/>. An empty input returns an empty set without a round trip.
    /// </summary>
    Task<IReadOnlySet<Guid>> WithCurrentMatchAsync(
        Guid cvVersionId,
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default);
}
