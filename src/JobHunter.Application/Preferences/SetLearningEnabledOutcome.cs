namespace JobHunter.Application.Preferences;

/// <summary>
/// The result of a <see cref="SetLearningEnabledHandler"/> invocation — a value, not an exception, because the
/// outcome is always an expected state the caller renders (coding-standards §4). <see cref="Changed"/> lets the
/// caller distinguish a real flip from an idempotent no-op, so a redelivered request reads honestly.
/// </summary>
/// <param name="Enabled">The state learning is now in.</param>
/// <param name="Changed"><c>true</c> when this request actually flipped the switch; <c>false</c> when it already held that state.</param>
public sealed record SetLearningEnabledOutcome(bool Enabled, bool Changed);
