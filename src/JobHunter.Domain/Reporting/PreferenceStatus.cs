namespace JobHunter.Domain.Reporting;

/// <summary>
/// The metadata of the most recent preference fit, for <c>/prefs</c> (F10 T08): how much evidence the latest
/// model was fitted on and whether that model is the active one shaping the ranking. It carries no weight and no
/// CV-derived value — only the counts that let the command state, below the evidence floor, how many more
/// signals are needed before learning turns on, and above it, that an active model exists to list.
/// </summary>
/// <param name="SignalCount">How many signals the latest fit was built on — checked against the activation floor.</param>
/// <param name="HasActiveModel">True when the latest model is active, so its weights shape the ranking.</param>
public sealed record PreferenceStatus(int SignalCount, bool HasActiveModel);
