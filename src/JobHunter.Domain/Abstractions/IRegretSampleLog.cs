namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The per-week idempotence gate for the regret sampler (F4 T21, ADR-F4-0003), the exact analogue of
/// <see cref="IRatingRoundLog"/> for the rating loop. <see cref="TryOpenAsync"/> inserts one row keyed by the
/// week the sample covers and returns <c>true</c> only on a genuine insert, so a redelivered or re-scheduled
/// weekly tick samples nothing, spends nothing at the cheap tier, and raises no duplicate alert — the sample
/// runs once per week (done-when 1).
///
/// <para>Append-only, mirroring the <c>rating_round_log</c> discipline: there is no update and no delete path,
/// because clearing a week would let a redelivery re-sample and double-spend. It carries nothing about the
/// Owner's CV.</para>
/// </summary>
public interface IRegretSampleLog
{
    /// <summary>
    /// Opens the regret sample for the week beginning <paramref name="weekStart"/>, returning <c>true</c> on a
    /// genuine insert and <c>false</c> when the week was already sampled. The unique <c>week_start</c>
    /// constraint arbitrates, so only the first tick for a week samples.
    /// </summary>
    Task<bool> TryOpenAsync(
        DateTimeOffset weekStart, DateTimeOffset openedAt, CancellationToken cancellationToken = default);
}
