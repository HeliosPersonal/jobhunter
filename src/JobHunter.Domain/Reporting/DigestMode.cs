namespace JobHunter.Domain.Reporting;

/// <summary>
/// Which of the four header shapes a <see cref="Digest"/> renders (F5 message contract §Header,
/// §Degraded-day variants; ADR-F5-0001). Every variant still arrives at 07:00 — a degraded day is a
/// different message, never a missing one.
///
/// <para>It is <strong>persisted on the digest</strong> (data-model §digests <c>mode</c>), resolved once at
/// assembly from the Run's state and counts (<see cref="Abstractions.IDigestScopeQuery"/> results). Delivery
/// is a replay of stored state, so a re-render — tomorrow, or from the <c>/digest</c> command — reproduces
/// the same header rather than re-classifying a run that has since moved on (SAD §4 S2). Persisted as
/// <c>text</c>, never an ordinal (coding-standards §5).</para>
/// </summary>
public enum DigestMode
{
    /// <summary>The normal morning: counts, the single best opportunity, the hidden line.</summary>
    Full,

    /// <summary>Nothing matched (AC-05): the "this is normal, everything is working" reassurance.</summary>
    NothingNew,

    /// <summary>Analysis did not finish (AC-06): a "(partial)" header and a "still being analysed" line.</summary>
    Partial,

    /// <summary>The daily budget was reached mid-run (AC-06 cost abort): a "(reduced)" header.</summary>
    BudgetReached,
}
