namespace JobHunter.Application.Preferences;

/// <summary>
/// A request from the Owner to turn preference learning on or off at runtime (T08, done-when 4, AC-07) — the
/// write path behind the API learning endpoint and the Telegram override command. It flips the persisted
/// <see cref="Domain.Abstractions.ILearningSwitch"/>; the change takes effect on the next ranking and is stated
/// on the next digest, without deleting any signal.
/// </summary>
/// <param name="Enabled">The state the Owner wants: <c>true</c> to learn, <c>false</c> to apply only explicit preferences.</param>
/// <param name="OccurredAt">When the Owner flipped it (from <c>IClock</c>, never <c>DateTime.Now</c>); recorded on the switch.</param>
public sealed record SetLearningEnabledCommand(bool Enabled, DateTimeOffset OccurredAt);
