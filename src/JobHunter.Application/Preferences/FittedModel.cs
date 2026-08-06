namespace JobHunter.Application.Preferences;

/// <summary>
/// The output of one <see cref="WeightFitter.Fit"/> (F7 SAD §5): the weights the fit produced and the count
/// of signals that were in-window and therefore actually fed it. An indifferent Owner produces an empty
/// <see cref="Weights"/> list — that is a valid, important result, not an error (test-plan). The learner
/// (T05) decides, from <see cref="SignalCount"/> against the activation floor, whether the resulting model
/// may be turned on.
/// </summary>
public sealed record FittedModel(IReadOnlyList<FittedWeight> Weights, int SignalCount);
