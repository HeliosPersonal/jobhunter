namespace JobHunter.Application.Preferences;

/// <summary>
/// Which business outcome a <see cref="ResetPreferenceModelCommand"/> produced — a value, not an exception,
/// because each is an expected result the caller renders (coding-standards §4). The API maps them to
/// <c>200</c> and <c>404</c>; Telegram maps them to distinct replies.
/// </summary>
public enum ResetPreferenceModelResult
{
    /// <summary>An active model was found and switched off; F4 falls back to the explicit-preference floor.</summary>
    Reset,

    /// <summary>No model was active — nothing was changed.</summary>
    NothingActive,
}

/// <summary>
/// The result of a <see cref="ResetPreferenceModelHandler"/> invocation. On success it echoes the
/// <see cref="DeactivatedVersion"/> so the caller can confirm exactly which learned model was switched off.
/// </summary>
/// <param name="Result">Which of the two outcomes occurred.</param>
/// <param name="DeactivatedVersion">On success, the version that was switched off; otherwise <c>null</c>.</param>
public sealed record ResetPreferenceModelOutcome(ResetPreferenceModelResult Result, int? DeactivatedVersion)
{
    public static ResetPreferenceModelOutcome Reset(int deactivatedVersion) =>
        new(ResetPreferenceModelResult.Reset, deactivatedVersion);

    public static ResetPreferenceModelOutcome NothingActive() =>
        new(ResetPreferenceModelResult.NothingActive, DeactivatedVersion: null);
}
