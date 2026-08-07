using System.Collections.Concurrent;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Application.Commands;

/// <summary>
/// The per-chat command rate limit (SAD §8: "20 commands/minute per chat, then one throttle message
/// until the window clears"). A fixed 60-second window per chat: the first <see cref="Budget"/> attempts
/// are <see cref="RateVerdict.Allowed"/>; the first attempt past the budget is
/// <see cref="RateVerdict.Throttled"/> — refused, and worth one throttle message — and every further
/// attempt in the same window is <see cref="RateVerdict.Silenced"/>, so the Owner is not answered with a
/// throttle reply per command (done-when #3). The window is anchored on its first attempt and clears
/// once 60 seconds elapse, at which point the budget is fresh.
///
/// <para>Clock-driven (<see cref="IClock"/>) so no test waits on real time, and a singleton holding one
/// small window per chat. There is one Owner, so the map holds a single entry in practice; the
/// per-<c>chatId</c> keying keeps the contract honest and the tests independent.</para>
/// </summary>
public sealed class CommandRateLimiter(IClock clock)
{
    /// <summary>Commands allowed per window (SAD §8).</summary>
    public const int Budget = 20;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ConcurrentDictionary<long, ChatWindow> _windows = new();
    private readonly object _gate = new();

    /// <summary>Records one command attempt for <paramref name="chatId"/> and returns whether to allow it.</summary>
    public RateVerdict Check(long chatId)
    {
        var now = _clock.UtcNow;

        lock (_gate)
        {
            var window = _windows.TryGetValue(chatId, out var existing) && now - existing.StartedAt < Window
                ? existing
                : new ChatWindow(now, 0);

            var count = window.Count + 1;
            _windows[chatId] = window with { Count = count };

            // The (Budget + 1)-th attempt is the one throttle message; anything beyond is silenced.
            return count <= Budget
                ? RateVerdict.Allowed
                : count == Budget + 1 ? RateVerdict.Throttled : RateVerdict.Silenced;
        }
    }

    // The count so far in the current window, and when that window began.
    private readonly record struct ChatWindow(DateTimeOffset StartedAt, int Count);
}
