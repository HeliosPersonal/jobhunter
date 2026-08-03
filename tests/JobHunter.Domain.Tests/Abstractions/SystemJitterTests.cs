using JobHunter.Domain.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Abstractions;

/// <summary>
/// T11: the jitter applied to the poll backoff so several batches submitted together do not poll in
/// lockstep (F3 SAD §8, test-plan §NFR). Jitter only ever <em>extends</em> a delay — never shortens it
/// below the schedule — so the backoff ceiling is honoured while the spread breaks synchrony.
/// </summary>
public sealed class SystemJitterTests
{
    [Fact]
    public void Apply_never_returns_less_than_the_base_delay()
    {
        var jitter = new SystemJitter();
        var baseDelay = TimeSpan.FromMinutes(15);

        for (var i = 0; i < 1_000; i++)
        {
            jitter.Apply(baseDelay).ShouldBeGreaterThanOrEqualTo(baseDelay);
        }
    }

    [Fact]
    public void Apply_stays_within_the_base_delay_plus_its_jitter_fraction()
    {
        var jitter = new SystemJitter();
        var baseDelay = TimeSpan.FromMinutes(10);
        var ceiling = baseDelay * (1 + SystemJitter.JitterFraction);

        for (var i = 0; i < 1_000; i++)
        {
            jitter.Apply(baseDelay).ShouldBeLessThanOrEqualTo(ceiling);
        }
    }

    [Fact]
    public void Apply_spreads_identical_base_delays_so_they_do_not_poll_in_lockstep()
    {
        var jitter = new SystemJitter();
        var baseDelay = TimeSpan.FromMinutes(15);

        var spread = Enumerable.Range(0, 100).Select(_ => jitter.Apply(baseDelay)).Distinct().Count();

        // A single fixed value would collapse to one; jitter must produce a spread.
        spread.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Apply_of_a_zero_base_delay_is_zero()
    {
        new SystemJitter().Apply(TimeSpan.Zero).ShouldBe(TimeSpan.Zero);
    }
}
