using JobHunter.Application.Enrichment;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Enrichment;

/// <summary>
/// T11: the poll backoff schedule as a pure function (F3 SAD §8, test-plan §NFR). The schedule is
/// asserted here with no clock and no waiting — 2 min doubling to a 15 min ceiling — which is what makes
/// the poller's timing a unit-testable value rather than an emergent property of sleeps. Jitter is a
/// separate concern applied on top (<see cref="JobHunter.Domain.Abstractions.IJitter"/>), so the base
/// schedule stays deterministic and the "no lockstep" property is tested independently.
/// </summary>
public sealed class PollBackoffTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 15)]
    [InlineData(5, 15)]
    [InlineData(6, 15)]
    [InlineData(50, 15)]
    public void DelayForAttempt_doubles_from_two_minutes_and_caps_at_fifteen(int attempt, int expectedMinutes)
    {
        PollBackoff.DelayForAttempt(attempt).ShouldBe(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void The_first_five_attempts_are_the_specified_schedule()
    {
        var schedule = Enumerable.Range(1, 5).Select(PollBackoff.DelayForAttempt).ToList();

        schedule.ShouldBe(
        [
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(15),
        ]);
    }

    [Fact]
    public void An_attempt_below_one_is_a_programmer_error()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PollBackoff.DelayForAttempt(0));
    }
}
