namespace JobHunter.Application.Reporting;

/// <summary>
/// Tunables for digest assembly (F5 SAD §6.1). Bound and validated at startup (coding-standards §options).
/// The card threshold and cap are the two numbers that decide what the Owner sees: a job is shown only when
/// its final score reaches <see cref="CardScoreThreshold"/>, and at most <see cref="MaxCards"/> cards are
/// shown so a strong day does not bury the best under the merely-good. Both are config-driven so the Owner
/// can tune the digest's selectivity without a deploy.
///
/// <para>The threshold is distinct from F4's ranking-time presentation floor (40): that decides what is
/// <em>suppressed</em> and counted in the footer; this decides which of the <em>shown</em> scores are strong
/// enough to become a card. A score between the two is neither suppressed nor carded — it is simply below the
/// digest's bar.</para>
/// </summary>
public sealed class DigestOptions
{
    public const string SectionName = "Digest";

    /// <summary>
    /// The minimum final score a job needs to become a card (F5 SAD §6.1). The default 70 is the strong-match
    /// bar the header also counts against, so the cards and the "N strong matches" line agree by construction.
    /// </summary>
    public decimal CardScoreThreshold { get; init; } = 70m;

    /// <summary>The most cards a digest shows (F5 SAD §6.1); the default 10 is what a half-awake triage tolerates.</summary>
    public int MaxCards { get; init; } = 10;

    /// <summary>
    /// The card floor (F7 QG-3): the fewest cards a digest must contain so learning can never empty it. When
    /// suppression would leave fewer than this, the least-suppressed jobs are restored to reach it and the
    /// digest states how many (<see cref="Domain.Reporting.Digest.RestoredCount"/>). The default 3 matches the
    /// SAD's "fewer than three cards" rule. A restoration is display-only: a restored job's score row stays
    /// suppressed, so the footer's count still reconciles to the database (invariant 11).
    /// </summary>
    public int MinCards { get; init; } = 3;

    /// <summary>
    /// The fewest salaried jobs the header's average is built from — below it the average is null, because a
    /// mean of one or two figures is more misleading than absent (data-model §digests, AC on avg_salary).
    /// </summary>
    public int MinSalariesForAverage { get; init; } = 3;
}
