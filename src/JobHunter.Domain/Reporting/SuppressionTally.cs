using JobHunter.Domain.Common;

namespace JobHunter.Domain.Reporting;

/// <summary>
/// One line of the footer's suppression breakdown: a reason and how many jobs it hid (data-model §digests
/// <c>suppression_breakdown</c>). This is what makes [[DECISION-LOG|D7]] and invariant 11 real — a silent
/// filter is indistinguishable from a bug, so every suppressed job is counted under a stated reason and the
/// digest reports it. A tally with a blank reason or a negative count is unrepresentable.
/// </summary>
public sealed class SuppressionTally : ValueObject
{
    public static readonly Error BlankReason =
        new("digest.suppression_tally.blank_reason", "A suppression tally must state a non-blank reason (invariant 11).");

    public static readonly Error NegativeCount =
        new("digest.suppression_tally.negative_count", "A suppression tally count must not be negative.");

    private SuppressionTally(string reason, int count)
    {
        Reason = reason;
        Count = count;
    }

    /// <summary>Why these jobs were withheld — never blank (invariant 11).</summary>
    public string Reason { get; }

    /// <summary>How many jobs this reason hid — never negative.</summary>
    public int Count { get; }

    public static Result<SuppressionTally> TryCreate(string? reason, int count)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return BlankReason;
        }

        if (count < 0)
        {
            return NegativeCount;
        }

        return Result<SuppressionTally>.Success(new SuppressionTally(reason.Trim(), count));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Reason;
        yield return Count;
    }
}
