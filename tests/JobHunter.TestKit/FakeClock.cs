using JobHunter.Domain.Abstractions;

namespace JobHunter.TestKit;

/// <summary>
/// A controllable <see cref="IClock"/> so no test waits on real time or depends on the real date
/// (testing conventions). Time only moves when a test moves it.
/// </summary>
public sealed class FakeClock : IClock
{
    /// <summary>The default anchor: a fixed, DST-neutral instant so tests are deterministic.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

    public FakeClock() => UtcNow = DefaultNow;

    public FakeClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Moves the clock forward by <paramref name="delta"/> and returns the new instant.</summary>
    public DateTimeOffset Advance(TimeSpan delta)
    {
        UtcNow += delta;
        return UtcNow;
    }

    /// <summary>Sets the clock to an absolute instant.</summary>
    public void Set(DateTimeOffset now) => UtcNow = now;
}
