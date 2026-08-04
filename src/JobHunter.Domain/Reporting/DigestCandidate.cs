namespace JobHunter.Domain.Reporting;

/// <summary>
/// One scored job considered for a <see cref="Digest"/> (F5 SAD §6.1, data-model §scores/§matches). The read
/// side of "the Run's scores joined to their current match": the final ordering score, whether it was
/// suppressed and why, the match reasons that would become the card's explanation (invariant 4), and the
/// job's expected pay normalised to USD when it is USD-denominated (for the header's average).
///
/// <para>It carries <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is
/// not this one (F4 invariant). <see cref="Reasons"/> is the snapshot the card copies at assembly, so a later
/// re-score cannot change a delivered digest. <see cref="SalaryUsd"/> is null unless the match's salary
/// expectation is present and stated in USD — there is no FX conversion, because a fabricated rate is worse
/// than an absent average.</para>
/// </summary>
/// <param name="JobId">The scored job; the card's subject and the deterministic tie-break key.</param>
/// <param name="FinalScore">The final 0–100 ordering score, snapshotted onto the card.</param>
/// <param name="Suppressed">True when the score was withheld from the digest with a reason (invariant 11).</param>
/// <param name="SuppressionReason">Why it was withheld, or null when it was shown.</param>
/// <param name="Reasons">The current match's reasons; a card without at least one is excluded (AC-02).</param>
/// <param name="SalaryUsd">The USD midpoint of the match's salary expectation, or null.</param>
/// <param name="ApplyUrl">
/// The job's apply destination, verified at assembly before the card is presented (AC-11). It is the one URL
/// the card links to, not the CV or anything about the Owner — the CV crosses exactly one boundary, and it is
/// not this one (F4 invariant).
/// </param>
public sealed record DigestCandidate(
    Guid JobId,
    decimal FinalScore,
    bool Suppressed,
    string? SuppressionReason,
    IReadOnlyList<string> Reasons,
    decimal? SalaryUsd,
    string ApplyUrl);
