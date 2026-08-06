using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Preferences;

/// <summary>
/// The output of <see cref="PreferenceComponentCalculator.Calculate"/> (F7 T06): the learned-preference
/// component of the F4 score in <c>[0,1]</c>, the per-dimension contributions the score row records so the
/// number is reconstructable (QG-1), and any conflict where an explicit Profile preference overrode a
/// contradicting learned weight (AC-05). A job the model has no applicable opinion on produces <c>null</c>
/// rather than a <c>PreferenceComponent</c>, so F4 renormalises the preference weight away instead of scoring
/// the job at a neutral 0.5.
/// </summary>
/// <param name="Value">The preference component in <c>[0,1]</c> — <c>(clamp(netPull, -1, 1) + 1) / 2</c>.</param>
/// <param name="Contributions">The applied weights, one per matched dimension value, that produced the pull.</param>
/// <param name="Conflicts">Explicit-over-learned overrides recorded for the score row and the digest (AC-05).</param>
public sealed record PreferenceComponent(
    decimal Value,
    IReadOnlyList<PreferenceContribution> Contributions,
    IReadOnlyList<PreferenceConflict> Conflicts);

/// <summary>
/// One learned weight that applied to a job because its <see cref="JobFacts"/> carried the weight's
/// <c>(dimension, value)</c> (F7 T06, QG-1). Recorded on the score so "why did this rank where it did?"
/// is answerable weight by weight, not just as a single number.
/// </summary>
/// <param name="Dimension">The dimension the weight is about.</param>
/// <param name="Value">The dimension value the job matched on.</param>
/// <param name="Weight">The signed pull the weight contributed, in <c>[-1, +1]</c>.</param>
public sealed record PreferenceContribution(Dimension Dimension, string Value, decimal Weight);

/// <summary>
/// A recorded case where an explicit Profile preference contradicted a learned weight on the same
/// <c>(dimension, value)</c>, so the explicit one won and the learned weight was dropped from the component
/// (AC-05). Kept so the conflict is visible in the explainability view rather than silently resolved.
/// </summary>
/// <param name="Dimension">The dimension both preferences are about.</param>
/// <param name="Value">The dimension value in contention.</param>
/// <param name="LearnedWeight">The signed learned weight that was overridden.</param>
public sealed record PreferenceConflict(Dimension Dimension, string Value, decimal LearnedWeight);

/// <summary>
/// A preference the Owner stated <em>outright</em> in the Profile, projected into the same
/// <c>(dimension, value)</c> vocabulary the learner uses (F7 T06). <see cref="IsPositive"/> is the stated
/// polarity — the Owner wants this value (a preferred country, an accepted employment type) or is avoiding it.
/// The calculator lets an explicit stance override a contradicting learned weight (AC-05); it never itself
/// contributes a magnitude, because explicit preferences shape ranking through the Profile's own rules, not
/// through the learned component.
/// </summary>
/// <param name="Dimension">The dimension the Owner stated a preference on.</param>
/// <param name="Value">The dimension value the stated preference is about.</param>
/// <param name="IsPositive">True when the Owner wants the value; false when avoiding it.</param>
public sealed record ExplicitStance(Dimension Dimension, string Value, bool IsPositive);
