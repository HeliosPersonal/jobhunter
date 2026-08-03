namespace JobHunter.Application.Enrichment;

/// <summary>
/// The batch poll backoff schedule as a pure function (F3 SAD §8, S5): 2 min doubling to a 15 min
/// ceiling. It is a value, not a sleep — the poller re-enqueues itself with the delay this returns, so
/// the whole schedule is asserted against a <c>FakeClock</c> with no real waiting (test-plan §NFR). The
/// 6 h cap is a total-elapsed concern owned by the poller (it marks the batch failed and carries its
/// items over), not part of the per-attempt delay computed here.
/// </summary>
public static class PollBackoff
{
    /// <summary>The first attempt's delay — the schedule doubles from here.</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

    /// <summary>The delay ceiling — once reached, every later attempt waits this long.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The delay before the <paramref name="attempt"/>-th poll (1-based): attempt 1 waits
    /// <see cref="InitialDelay"/>, each subsequent attempt doubles the previous, clamped at
    /// <see cref="MaxDelay"/>. So the sequence is 2, 4, 8, 15, 15… minutes.
    /// </summary>
    public static TimeSpan DelayForAttempt(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        // Double InitialDelay (attempt-1) times, but stop multiplying the moment the ceiling is reached so
        // a large attempt number cannot overflow the intermediate product.
        var delay = InitialDelay;
        for (var i = 1; i < attempt && delay < MaxDelay; i++)
        {
            delay += delay;
        }

        return delay < MaxDelay ? delay : MaxDelay;
    }
}
