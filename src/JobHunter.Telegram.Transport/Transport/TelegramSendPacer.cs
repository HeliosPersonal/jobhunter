using JobHunter.Domain.Abstractions;

namespace JobHunter.Telegram.Transport;

/// <summary>
/// The pacing arithmetic for outbound sends (SAD §7), kept pure over <see cref="IClock"/> so it is unit
/// tested by advancing a fake clock rather than by waiting on real time. It tracks the earliest instant the
/// next send is allowed to leave: <see cref="ReserveSlot"/> hands out that slot and advances it by the
/// minimum interval, so back-to-back sends queue one interval apart and stay inside Telegram's
/// 30-messages-per-second limit. <see cref="Penalise"/> pushes the slot out by a <c>429</c>'s
/// <c>retry_after</c> and never shortens an existing, longer wait — a provider's cool-off is honoured
/// exactly (AC of T07), never overridden by our own shorter spacing.
/// </summary>
internal sealed class TelegramSendPacer(IClock clock, TimeSpan minInterval)
{
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly TimeSpan _minInterval = minInterval;
    private readonly object _gate = new();
    private DateTimeOffset _nextAllowedSend = DateTimeOffset.MinValue;

    /// <summary>
    /// Reserves the next send slot and returns how long the caller must wait before sending. The slot is
    /// advanced by the minimum interval as it is handed out, so a second caller reserving immediately after
    /// gets a slot one interval later — the reservation is the queue.
    /// </summary>
    public TimeSpan ReserveSlot()
    {
        lock (_gate)
        {
            var now = _clock.UtcNow;
            var sendAt = _nextAllowedSend > now ? _nextAllowedSend : now;
            _nextAllowedSend = sendAt + _minInterval;
            return sendAt - now is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Applies a <c>429 retry_after</c> cool-off: no send leaves before <paramref name="retryAfter"/> from
    /// now. The block is only ever extended, never shortened, so a longer provider cool-off already in force
    /// is honoured exactly.
    /// </summary>
    public void Penalise(TimeSpan retryAfter)
    {
        lock (_gate)
        {
            var until = _clock.UtcNow + retryAfter;
            if (until > _nextAllowedSend)
            {
                _nextAllowedSend = until;
            }
        }
    }
}
