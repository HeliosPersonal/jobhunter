using JobHunter.Domain.Preferences;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write port F6 stages an outcome <see cref="Signal"/> through (T08, AC-08). Unlike
/// <see cref="ISignalRepository"/> — which opens its own connection and commits immediately, as F5's card
/// action does — this port only <em>stages</em> the signal into the caller's current unit of work and never
/// commits. The F6 owner-action handler stages the signal and the transition together, then commits both in
/// the one EF transaction, so the weighted evidence and the status change are all-or-nothing (SAD §6.1): a
/// signal is never written for a transition that rolled back, and a transition never lands without its signal.
///
/// <para><see cref="IsStaged"/> lets the publisher skip a second signal for the same
/// <c>(job_id, kind, occurred_at)</c> already staged in this unit of work — the in-memory belt to the
/// database's unique-constraint braces, so a redelivered outcome adds no duplicate even before the commit.</para>
/// </summary>
public interface IOutcomeSignalWriter
{
    /// <summary>
    /// Stages <paramref name="signal"/> into the current unit of work. It is not persisted until the caller
    /// commits its transaction; nothing is written if the caller rolls back.
    /// </summary>
    void Stage(Signal signal);

    /// <summary>
    /// Whether a signal with the same <paramref name="jobId"/>, <paramref name="kind"/> and
    /// <paramref name="occurredAt"/> is already staged (or tracked) in this unit of work — used to keep a
    /// redelivered outcome from staging a duplicate before the database constraint would reject it.
    /// </summary>
    bool IsStaged(Guid jobId, SignalKind kind, DateTimeOffset occurredAt);
}
