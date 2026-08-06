using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Preferences;

/// <summary>
/// The function F4's ranking calls to turn the active model's learned weights into the preference component of
/// one job's score (F7 SAD §6.2, T06). A <strong>static, pure</strong> function of explicit values — the
/// model's weights, the job's <see cref="JobFacts"/>, and the Owner's explicit Profile stances — so its
/// determinism is provable rather than asserted, exactly like the <see cref="WeightFitter"/> whose output it
/// consumes. It changes no F4 file: F7 supplies a value F4 already accepts, it does not touch the formula.
///
/// <para>For each dimension value the job carries, the matching non-disabled weight contributes its signed
/// pull; the pulls sum, the sum is clamped to <c>[-1, +1]</c> (so the component is bounded regardless of the
/// model's contents — a degenerate over-1 model still lands in range), and mapped to <c>[0,1]</c> by
/// <c>(net + 1) / 2</c>, where 0.5 is indifference. Two rules keep it honest:</para>
///
/// <list type="bullet">
/// <item>A <strong>disabled</strong> weight is excluded up front, so the Owner switching a preference off
/// takes effect on the very next ranking (AC-06).</item>
/// <item>An <strong>explicit</strong> Profile stance that contradicts a learned weight on the same
/// <c>(dimension, value)</c> drops that weight and records a <see cref="PreferenceConflict"/> — explicit
/// always outranks inferred, and the conflict is visible rather than silently resolved (AC-05).</item>
/// </list>
///
/// <para>A job with no applicable weight left produces <c>null</c>: the model has no opinion on it, so F4
/// renormalises the preference weight away rather than scoring it at a neutral 0.5. A recorded conflict is
/// enough to return a component even when no weight survives, so the override is not lost.</para>
/// </summary>
public static class PreferenceComponentCalculator
{
    /// <summary>The midpoint the signed pull is centred on: 0.5 is indifference, mapping net 0 → 0.5.</summary>
    private const decimal Midpoint = 0.5m;

    /// <summary>
    /// Computes the preference component for one job. <paramref name="weights"/> are the active model's
    /// weights, <paramref name="facts"/> the job's characteristics in the dimension vocabulary, and
    /// <paramref name="explicitStances"/> the Owner's stated Profile preferences. Returns <c>null</c> when no
    /// weight applies and no conflict was recorded — the model has no opinion on this job.
    /// </summary>
    public static PreferenceComponent? Calculate(
        IReadOnlyList<PreferenceWeight> weights,
        JobFacts facts,
        IReadOnlyList<ExplicitStance> explicitStances)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(explicitStances);

        var contributions = new List<PreferenceContribution>();
        var conflicts = new List<PreferenceConflict>();

        foreach (var weight in weights)
        {
            if (weight.Disabled)
            {
                // The Owner switched this preference off — it stops affecting ordering immediately (AC-06).
                continue;
            }

            if (!facts.ValuesFor(weight.Dimension).Contains(weight.Value, StringComparer.Ordinal))
            {
                // The job does not carry this value, so the weight simply does not apply to it.
                continue;
            }

            if (Contradicts(explicitStances, weight))
            {
                // Explicit Profile preference outranks the inferred one, always (AC-05). Drop the learned
                // weight and record the conflict so it is visible, not silently resolved.
                conflicts.Add(new PreferenceConflict(weight.Dimension, weight.Value, weight.Weight));
                continue;
            }

            contributions.Add(new PreferenceContribution(weight.Dimension, weight.Value, weight.Weight));
        }

        if (contributions.Count == 0 && conflicts.Count == 0)
        {
            return null;
        }

        var net = Clamp(contributions.Sum(c => c.Weight), PreferenceWeight.MinWeight, PreferenceWeight.MaxWeight);
        var value = Midpoint + (net * Midpoint);

        return new PreferenceComponent(value, contributions, conflicts);
    }

    /// <summary>
    /// True when an explicit stance names the weight's <c>(dimension, value)</c> with the opposite polarity —
    /// the Owner wants a value the model learned to penalise, or is avoiding one the model learned to reward.
    /// A stance of the same polarity agrees and is not a conflict.
    /// </summary>
    private static bool Contradicts(IReadOnlyList<ExplicitStance> stances, PreferenceWeight weight) =>
        stances.Any(s =>
            s.Dimension == weight.Dimension
            && string.Equals(s.Value, weight.Value, StringComparison.Ordinal)
            && s.IsPositive != weight.Weight >= 0);

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        value < min ? min : value > max ? max : value;
}
