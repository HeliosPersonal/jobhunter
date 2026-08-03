using JobHunter.Domain.Common;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The weights of the linear ranking formula (SAD §8, ADR-F4-0001):
/// <c>final = 100 × (w_m·match + w_p·preference + w_f·freshness) × confidence</c>. They are
/// <em>configuration</em> — documented, tunable without a deploy, never model-controlled — and they sum
/// to 1 so the weighted term is a convex combination in <c>[0,1]</c> before the confidence multiplier.
///
/// <para>A value object: two weight sets are equal by their components, and a set whose weights are out
/// of range or do not sum to 1 cannot be constructed. F7 tunes only the preference weight; F4 T14 adds
/// an alignment weight by superseding this record's shape in that task.</para>
/// </summary>
public sealed class RankingWeights : ValueObject
{
    /// <summary>The largest reconciliation gap tolerated when checking the weights sum to 1.</summary>
    private const decimal SumTolerance = 0.0001m;

    public RankingWeights(decimal match, decimal preference, decimal freshness)
    {
        EnsureFraction(match, nameof(match));
        EnsureFraction(preference, nameof(preference));
        EnsureFraction(freshness, nameof(freshness));

        if (Math.Abs(match + preference + freshness - 1m) > SumTolerance)
        {
            throw new ArgumentException(
                $"Ranking weights must sum to 1 (got {match + preference + freshness}).",
                nameof(match));
        }

        Match = match;
        Preference = preference;
        Freshness = freshness;
    }

    /// <summary>The default weights (SAD §8): match 0.60, preference 0.25, freshness 0.15.</summary>
    public static RankingWeights Default { get; } = new(0.60m, 0.25m, 0.15m);

    public decimal Match { get; }

    public decimal Preference { get; }

    public decimal Freshness { get; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Match;
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
