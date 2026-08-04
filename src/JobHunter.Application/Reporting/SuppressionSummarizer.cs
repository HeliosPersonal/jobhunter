using JobHunter.Domain.Reporting;

namespace JobHunter.Application.Reporting;

/// <summary>
/// The pure grouping of a Run's suppressed candidates into the footer's breakdown (F5 SAD §6.1, AC-07,
/// invariant 11). It takes every suppressed <see cref="DigestCandidate"/> and returns one
/// <see cref="SuppressionTally"/> per distinct reason with its count, so the footer can state "34 hidden:
/// 20 below threshold, 14 off-target" and the numbers reconcile — a silent filter is indistinguishable from
/// a bug, and this is what makes [[DECISION-LOG|D7]] real.
///
/// <para>Free of I/O and time, so it is exhaustively unit-testable. A suppressed candidate always carries a
/// reason (the ranking stage guarantees it, invariant 11); one that somehow does not is folded under a
/// single explicit "Unspecified" bucket rather than dropped, because losing a suppressed job from the count
/// is precisely the failure the breakdown exists to prevent. Tallies are ordered by descending count then
/// reason, so the largest bucket leads and the order is deterministic.</para>
/// </summary>
public static class SuppressionSummarizer
{
    /// <summary>The bucket a suppressed candidate with no stated reason falls into — never silently dropped.</summary>
    public const string UnspecifiedReason = "Unspecified";

    /// <summary>
    /// Groups the suppressed candidates by reason into a reconciling breakdown. Shown candidates are ignored.
    /// The returned counts sum to the number of suppressed candidates, which is what the <see cref="Digest"/>
    /// constructor asserts against its <c>suppressed_count</c> (invariant 11).
    /// </summary>
    public static IReadOnlyList<SuppressionTally> Summarize(IEnumerable<DigestCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(c => c.Suppressed)
            .GroupBy(c => string.IsNullOrWhiteSpace(c.SuppressionReason)
                ? UnspecifiedReason
                : c.SuppressionReason!.Trim())
            .Select(g => SuppressionTally.TryCreate(g.Key, g.Count()))
            .Where(result => result.IsSuccess)
            .Select(result => result.Value)
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Reason, StringComparer.Ordinal)
            .ToList();
    }
}
