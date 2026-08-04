using JobHunter.Domain.Common;

namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The five named inputs a <see cref="Score"/> is built from (data-model §scores, QG-1): the normalised
/// match component, the career-alignment component (TUNE-01/F4 T14), the preference component from the
/// active F7 model, the exponential-decay freshness component, and the confidence multiplier (1.00 with
/// an enrichment, 0.85 without — AC-09). Every one is stored so a score can be <em>reconstructed</em>
/// from its parts, which is what makes QG-1 a test rather than a promise.
///
/// <para>The four weighted components are fractions in <c>[0,1]</c>; the confidence multiplier is in
/// <c>(0,1]</c>. A value out of range is a programmer error — the arithmetic upstream produced nonsense —
/// so construction throws rather than clamping.</para>
/// </summary>
public sealed class ScoreComponents : ValueObject
{
    public ScoreComponents(
        decimal match,
        decimal alignment,
        decimal preference,
        decimal freshness,
        decimal confidenceMultiplier)
    {
        EnsureFraction(match, nameof(match));
        EnsureFraction(alignment, nameof(alignment));
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
        Alignment = alignment;
        Preference = preference;
        Freshness = freshness;
        ConfidenceMultiplier = confidenceMultiplier;
    }

    /// <summary>The normalised match score in <c>[0,1]</c>, before weighting.</summary>
    public decimal Match { get; }

    /// <summary>
    /// The career-alignment component in <c>[0,1]</c> (TUNE-01/F4 T14): how well the role's AI-usage and
    /// role-family match the Owner's AI-platform / staff trajectory. 0 for an anti-goal family with no
    /// AI usage; 1.0 for a Tier-1 high-AI-usage role.
    /// </summary>
    public decimal Alignment { get; }

    /// <summary>The preference component in <c>[0,1]</c> from the active preference model; 0 when none is active.</summary>
    public decimal Preference { get; }

    /// <summary>The freshness component in <c>[0,1]</c>: <c>exp(-ageDays/7)</c>.</summary>
    public decimal Freshness { get; }

    /// <summary>The confidence multiplier: 1.00 with an enrichment, 0.85 without (AC-09).</summary>
    public decimal ConfidenceMultiplier { get; }

    /// <summary>
    /// The final 0–100 score these components and <paramref name="weights"/> reconcile to:
    /// <c>100 × (w_m·match + w_a·alignment + w_p·preference + w_f·freshness) × confidence</c>. The
    /// <see cref="Score"/> aggregate asserts its stored total equals this (QG-1), so the arithmetic
    /// lives in one place.
    /// </summary>
    public decimal Reconcile(RankingWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var weighted =
            (weights.Match * Match)
            + (weights.Alignment * Alignment)
            + (weights.Preference * Preference)
            + (weights.Freshness * Freshness);

        return 100m * weighted * ConfidenceMultiplier;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Match;
        yield return Alignment;
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
