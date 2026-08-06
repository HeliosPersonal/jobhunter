namespace JobHunter.Application.Preferences;

/// <summary>
/// The tunables of one fit (F7 SAD §8), passed in rather than read from a clock or configuration so
/// <see cref="WeightFitter"/> stays a pure function the synthetic-behaviour corpus can drive deterministically.
/// <see cref="ReferenceTime"/> is the "now" recency decays from — a parameter, never <c>DateTimeOffset.Now</c>
/// — so the changed-mind profile can span a year in a millisecond.
/// </summary>
public sealed record FittingOptions(DateTimeOffset ReferenceTime)
{
    /// <summary>The recency half-life: a signal this old counts half as much as one at the reference time (SAD §8).</summary>
    public TimeSpan RecencyHalfLife { get; init; } = TimeSpan.FromDays(60);

    /// <summary>The fitting window: signals older than this contribute nothing (SAD §8, 180 days).</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromDays(180);

    /// <summary>
    /// The indifference deadband. A value whose recency-weighted positive rate lands within this distance of
    /// 0.5 is treated as no preference at all and earns no weight — the indifferent Owner produces nothing
    /// (test-plan, the indifferent profile). Symmetric about 0.5.
    /// </summary>
    public decimal IndifferenceBand { get; init; } = 0.05m;

    /// <summary>The ceiling on any one dimension's total contribution to the preference component (SAD §8, AC-09).</summary>
    public decimal MaxDimensionShare { get; init; } = 0.40m;
}
