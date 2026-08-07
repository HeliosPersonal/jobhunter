namespace JobHunter.Application.Preferences;

/// <summary>
/// Which business outcome a <see cref="DisablePreferenceWeightCommand"/> produced — a value, not an exception,
/// because each is an expected result the caller renders (coding-standards §4). The API maps them to
/// <c>200</c> and <c>404</c>; Telegram maps them to distinct replies.
/// </summary>
public enum DisablePreferenceWeightResult
{
    /// <summary>The weight was found and switched off (or already was); it stops affecting the next ranking.</summary>
    Disabled,

    /// <summary>No active model carries a weight with that id — nothing was changed.</summary>
    WeightNotFound,
}

/// <summary>
/// The result of a <see cref="DisablePreferenceWeightHandler"/> invocation. On success it echoes the
/// disabled weight's one-sentence <see cref="Explanation"/> (<see cref="WeightExplanation"/>), so the caller
/// can confirm exactly which preference was switched off without a second read.
/// </summary>
/// <param name="Result">Which of the two outcomes occurred.</param>
/// <param name="Explanation">On success, the disabled weight's one-sentence rendering; otherwise <c>null</c>.</param>
public sealed record DisablePreferenceWeightOutcome(DisablePreferenceWeightResult Result, string? Explanation)
{
    public static DisablePreferenceWeightOutcome Disabled(string explanation) =>
        new(DisablePreferenceWeightResult.Disabled, explanation);

    public static DisablePreferenceWeightOutcome NotFound() =>
        new(DisablePreferenceWeightResult.WeightNotFound, Explanation: null);
}
