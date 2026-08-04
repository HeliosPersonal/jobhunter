using JobHunter.Domain.Common;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The weights of the linear ranking formula (SAD §8, ADR-F4-0001, TUNE-01/F4 T14):
/// <c>final = 100 × (w_m·match + w_a·alignment + w_p·preference + w_f·freshness) × confidence</c>. They
/// are <em>configuration</em> — documented, tunable without a deploy, never model-controlled — and they
/// sum to 1 so the weighted term is a convex combination in <c>[0,1]</c> before the confidence multiplier.
///
/// <para>A value object: two weight sets are equal by their components, and a set whose weights are out
/// of range or do not sum to 1 cannot be constructed. F7 tunes only the preference weight. The
/// <c>alignment</c> term (T14) rewards the Owner's AI-platform / platform / staff trajectory so fit-to-CV
/// no longer buries aspiration — ADR-F4-0001 explicitly permits adding score components, so this is a
/// tuning change, not a rearchitecture.</para>
/// </summary>
public sealed class RankingWeights : ValueObject
{
    /// <summary>The largest reconciliation gap tolerated when checking the weights sum to 1.</summary>
    private const decimal SumTolerance = 0.0001m;

    public RankingWeights(decimal match, decimal alignment, decimal preference, decimal freshness)
    {
        EnsureFraction(match, nameof(match));
        EnsureFraction(alignment, nameof(alignment));
        EnsureFraction(preference, nameof(preference));
        EnsureFraction(freshness, nameof(freshness));

        var sum = match + alignment + preference + freshness;
        if (Math.Abs(sum - 1m) > SumTolerance)
        {
            throw new ArgumentException($"Ranking weights must sum to 1 (got {sum}).", nameof(match));
        }

        Match = match;
        Alignment = alignment;
        Preference = preference;
        Freshness = freshness;
    }

    /// <summary>The default weights (TUNE-01): match 0.45, alignment 0.20, preference 0.20, freshness 0.15.</summary>
    public static RankingWeights Default { get; } = new(0.45m, 0.20m, 0.20m, 0.15m);

    public decimal Match { get; }

    public decimal Alignment { get; }

    public decimal Preference { get; }

    public decimal Freshness { get; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Match;
        yield return Alignment;
        yield return Preference;
        yield return Freshness;
    }

    private static void EnsureFraction(decimal weight, string name)
    {
        if (weight is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(name, weight, "A ranking weight must be in [0, 1].");
        }
    }
}
