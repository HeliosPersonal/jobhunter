namespace JobHunter.Domain.Reporting;

/// <summary>
/// One job the most recent Run withheld from the digest (F7 T08 <c>/hidden</c>, done-when 6, risk D3). It
/// pairs the job's display facts with the reason it was suppressed, so the Owner can see what was hidden and
/// why — making suppression regret measurable rather than silent (invariant 11). Retrievable-then-actioned is
/// how a wrong learned weight is caught: a job the Owner would have wanted, hidden and then opened from
/// <c>/hidden</c>, is the signal that the model over-suppressed.
///
/// <para>It carries the raw display primitives the command renders, and <strong>nothing about the Owner's
/// CV</strong> — the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
/// <param name="JobId">The suppressed job.</param>
/// <param name="Title">The job title (raw board text; escaped and truncated by the formatter).</param>
/// <param name="Company">The company display name.</param>
/// <param name="Score">The role's final 0–100 score, so the Owner sees how close it came.</param>
/// <param name="SuppressionReason">Why it was withheld — never blank (invariant 11, AC-05).</param>
public sealed record HiddenJob(
    Guid JobId,
    string Title,
    string Company,
    decimal Score,
    string SuppressionReason);
