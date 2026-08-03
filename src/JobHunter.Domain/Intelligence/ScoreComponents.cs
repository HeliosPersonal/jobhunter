using JobHunter.Domain.Common;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The four named inputs a <see cref="Score"/> is built from (data-model §scores, QG-1): the normalised
/// match component, the preference component from the active F7 model, the exponential-decay freshness
/// component, and the confidence multiplier (1.00 with an enrichment, 0.85 without — AC-09). Every one
/// is stored so a score can be <em>reconstructed</em> from its parts, which is what makes QG-1 a test
/// rather than a promise.
///
/// <para>The three weighted components are fractions in <c>[0,1]</c>; the confidence multiplier is in
/// <c>(0,1]</c>. A value out of range is a programmer error — the arithmetic upstream produced nonsense —
/// so construction throws rather than clamping.</para>
/// </summary>
public sealed class ScoreComponents : ValueObject
{
    public ScoreComponents(
        decimal match,
        decimal preference,
        decimal freshness,
        decimal confidenceMultiplier)
    {
        EnsureFraction(match, nameof(match));
        EnsureFraction(preference, nameof(preference));
        EnsureFraction(freshness, nameof(freshness));

        if (confidenceMultiplier is <= 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidenceMultiplier),
                confidenceMultiplier,
                "The confidence multiplier must be in (0, 1].");
        }

        Match = match;
        Preference = preference;
        Freshness = freshness;
        ConfidenceMultiplier = confidenceMultiplier;
    }

    /// <summary>The normalised match score in <c>[0,1]</c>, before weighting.</summary>
    public decimal Match { get; }

    /// <summary>The preference component in <c>[0,1]</c> from the active preference model; 0 when none is active.</summary>
    public decimal Preference { get; }

    /// <summary>The freshness component in <c>[0,1]</c>: <c>exp(-ageDays/7)</c>.</summary>
    public decimal Freshness { get; }

    /// <summary>The confidence multiplier: 1.00 with an enrichment, 0.85 without (AC-09).</summary>
    public decimal ConfidenceMultiplier { get; }

    /// <summary>
    /// The final 0–100 score these components and <paramref name="weights"/> reconcile to:
    /// <c>100 × (w_m·match + w_p·preference + w_f·freshness) × confidence</c>. The <see cref="Score"/>
    /// aggregate asserts its stored total equals this (QG-1), so the arithmetic lives in one place.
    /// </summary>
    public decimal Reconcile(RankingWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var weighted =
            (weights.Match * Match)
            + (weights.Preference * Preference)
            + (weights.Freshness * Freshness);

        return 100m * weighted * ConfidenceMultiplier;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Match;
        yield return Preference;
        yield return Freshness;
        yield return ConfidenceMultiplier;
    }

    private static void EnsureFraction(decimal value, string name)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(name, value, "A score component must be in [0, 1].");
        }
    }
}
