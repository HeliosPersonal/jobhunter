namespace JobHunter.Application.Preferences;

/// <summary>
/// A request from the Owner to reset preference learning (T08, done-when 3) — the write path behind the API
/// reset endpoint and the Telegram override command. It deactivates the active <see cref="Domain.Preferences.PreferenceModel"/>
/// wholesale, the coarse counterpart to disabling a single weight; no signal is deleted, so the next refit can
/// rebuild from the same evidence.
/// </summary>
/// <param name="OccurredAt">When the Owner reset (from <c>IClock</c>, never <c>DateTime.Now</c>); recorded in the log.</param>
public sealed record ResetPreferenceModelCommand(DateTimeOffset OccurredAt);
