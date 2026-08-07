namespace JobHunter.Application.Preferences;

/// <summary>
/// A request from the Owner to switch a specific learned weight off (T08, AC-06) — the write path behind both
/// the API <c>POST …/preferences/weights/{id}/disable</c> and the Telegram override command. Addressed by the
/// weight's id, which the explainability view surfaced alongside its one-sentence rendering, so the Owner
/// disables exactly the preference they were shown.
/// </summary>
/// <param name="WeightId">The weight to switch off.</param>
/// <param name="OccurredAt">When the Owner disabled it (from <c>IClock</c>, never <c>DateTime.Now</c>); recorded on the weight.</param>
public sealed record DisablePreferenceWeightCommand(Guid WeightId, DateTimeOffset OccurredAt);
