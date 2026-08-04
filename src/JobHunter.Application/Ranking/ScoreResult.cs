using JobHunter.Domain.Intelligence;

namespace JobHunter.Application.Ranking;

/// <summary>
/// The output of <see cref="ScoreCalculator.Calculate"/> (T07): the final 0–100 ordering key together with
/// the named components and the <em>effective</em> weights it was reconciled against. The effective weights
/// are the input weights when a preference model is present and the renormalised weights when it is not, so
/// a caller persisting a <see cref="Score"/> stores exactly what rebuilds the total (QG-1).
/// </summary>
/// <param name="JobId">The job this score belongs to; the tie-break key when finals are equal.</param>
/// <param name="FinalScore">The 0–100 ordering key: <c>100 × (w·components) × confidence</c>.</param>
/// <param name="Components">The named inputs the total reconciles from.</param>
/// <param name="EffectiveWeights">The weights actually applied — renormalised when no preference is present.</param>
/// <param name="PreferencePresent">True when a preference model contributed a component.</param>
public readonly record struct ScoreResult(
    Guid JobId,
    decimal FinalScore,
    ScoreComponents Components,
    RankingWeights EffectiveWeights,
    bool PreferencePresent);
