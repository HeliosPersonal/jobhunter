namespace JobHunter.Application.Matching;

/// <summary>
/// The named factual disqualifiers of the pre-match filter (ADR-F4-0003). Persisted nowhere directly — the
/// rule surfaces as a human reason on the suppressed <c>scores</c> row and drives the pre-match corpus's
/// per-rule assertions (test-plan §The pre-match reference corpus), where each excluded case names the rule
/// that must exclude it.
/// </summary>
public enum PreMatchRule
{
    /// <summary>The job's band is a definite region incompatible with the Owner's, and the role is not remote.</summary>
    Timezone,

    /// <summary>The job's stated engagement is a recognised type the Owner does not seek.</summary>
    EmploymentType,

    /// <summary>The job's level is two or more rungs below the Owner's on the individual-contributor ladder.</summary>
    SeniorityFloor,

    /// <summary>The estimated pay sits entirely below the floor, same currency, at high confidence.</summary>
    SalaryFloor,

    /// <summary>The job already carries a current match against the active CV version.</summary>
    Lifecycle,
}

/// <summary>
/// The outcome of <see cref="PreMatchFilter.Evaluate"/>: either a pass — the job proceeds to the deep tier — or
/// an exclusion naming the single <see cref="PreMatchRule"/> that disqualified it and the human reason recorded
/// on the suppressed <c>scores</c> row (invariant 11, AC-12). An excluding verdict always carries a non-blank
/// reason; a passing one carries none. The pairing is enforced at construction so a reasonless exclusion — or a
/// reasoned pass — is unrepresentable.
/// </summary>
public sealed record PreMatchVerdict
{
    private PreMatchVerdict(bool excluded, PreMatchRule? rule, string? reason)
    {
        Excluded = excluded;
        Rule = rule;
        Reason = reason;
    }

    /// <summary>The single passing verdict: the job clears every factual gate.</summary>
    public static PreMatchVerdict Pass { get; } = new(excluded: false, rule: null, reason: null);

    /// <summary>True when the job is excluded from deep-tier matching on a factual ground.</summary>
    public bool Excluded { get; }

    /// <summary>The rule that excluded the job, or null on a pass.</summary>
    public PreMatchRule? Rule { get; }

    /// <summary>The human reason recorded on the suppressed score, or null on a pass (invariant 11).</summary>
    public string? Reason { get; }

    /// <summary>
    /// Builds an excluding verdict for <paramref name="rule"/> with <paramref name="reason"/>. A blank reason is
    /// a programmer error: invariant 11 makes a reasonless suppression unrepresentable, so it throws.
    /// </summary>
    public static PreMatchVerdict Exclude(PreMatchRule rule, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new PreMatchVerdict(excluded: true, rule, reason.Trim());
    }
}
