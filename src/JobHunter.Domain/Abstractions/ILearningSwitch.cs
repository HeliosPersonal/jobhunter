namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The Owner's runtime master switch over preference learning (F7 PRD AC-07, T08 done-when 4). Distinct from
/// the startup <c>LearningOptions</c> default: turning learning off must take effect on the next ranking and be
/// stated on the next digest, so the live state is <em>persisted</em> and flipped at request time — through the
/// API learning endpoint or the Telegram override command — not read once at boot. The seed value is the
/// configured default; thereafter this port is the single source of truth both <c>PreferenceModelQuery</c> and
/// the digest assembler consult.
///
/// <para>When learning is off, ranking renormalises the learned preference weight away and orders on match,
/// freshness and the Owner's <em>explicit</em> Profile preferences alone, and the digest says so — a bad week
/// of inference is silenced wholesale without deleting a single signal (the evidence survives for when it is
/// turned back on). Single Owner: one flag, no per-tenant scoping (invariant 9).</para>
/// </summary>
public interface ILearningSwitch
{
    /// <summary>Whether preference learning currently shapes ranking. The live, persisted value — not the boot default.</summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new state. <paramref name="occurredAt"/> is from <c>IClock</c> (never <c>DateTime.Now</c>) and
    /// records when the Owner last flipped the switch. Idempotent at the store: writing the current value again
    /// is harmless, though the handler avoids a redundant write.
    /// </summary>
    Task SetAsync(bool enabled, DateTimeOffset occurredAt, CancellationToken cancellationToken = default);
}
