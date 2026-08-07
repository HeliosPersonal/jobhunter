using JobHunter.Application.Commands;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

public sealed class CommandRateLimiterTests
{
    private const long Chat = 4242;

    private readonly FakeClock _clock = new();
    private readonly CommandRateLimiter _limiter;

    public CommandRateLimiterTests() => _limiter = new CommandRateLimiter(_clock);

    [Fact]
    public void Allows_the_first_command() =>
        _limiter.Check(Chat).ShouldBe(RateVerdict.Allowed);

    [Fact]
    public void Allows_the_full_budget_of_twenty_in_a_window()
    {
        for (var i = 0; i < 20; i++)
        {
            _limiter.Check(Chat).ShouldBe(RateVerdict.Allowed);
        }
    }

    [Fact]
    public void Throttles_the_twenty_first_command_in_the_window()
    {
        for (var i = 0; i < 20; i++)
        {
            _limiter.Check(Chat);
        }

        _limiter.Check(Chat).ShouldBe(RateVerdict.Throttled);
    }

    [Fact]
    public void Silences_further_commands_in_the_same_window_so_there_is_one_message_per_window()
    {
        for (var i = 0; i < 21; i++)
        {
            _limiter.Check(Chat);
        }

        // Done-when #3: the 22nd and beyond in the window are silenced — the throttle message is sent once.
        _limiter.Check(Chat).ShouldBe(RateVerdict.Silenced);
        _limiter.Check(Chat).ShouldBe(RateVerdict.Silenced);
    }

    [Fact]
    public void Resets_the_budget_once_the_window_has_elapsed()
    {
        for (var i = 0; i < 21; i++)
        {
            _limiter.Check(Chat);
        }

        _clock.Advance(TimeSpan.FromSeconds(60));

        _limiter.Check(Chat).ShouldBe(RateVerdict.Allowed);
    }

    [Fact]
    public void Throttles_again_in_a_fresh_window_after_the_budget_is_spent_again()
    {
        for (var i = 0; i < 21; i++)
        {
            _limiter.Check(Chat);
        }

        _clock.Advance(TimeSpan.FromSeconds(60));

        for (var i = 0; i < 20; i++)
        {
            _limiter.Check(Chat).ShouldBe(RateVerdict.Allowed);
        }

        _limiter.Check(Chat).ShouldBe(RateVerdict.Throttled);
    }

    [Fact]
    public void Keeps_a_separate_budget_per_chat()
    {
        for (var i = 0; i < 21; i++)
        {
            _limiter.Check(Chat);
        }

        // A different chat is untouched by the first chat's spend.
        _limiter.Check(9999).ShouldBe(RateVerdict.Allowed);
    }

    [Fact]
    public void A_command_just_inside_the_window_still_counts_against_the_budget()
    {
        for (var i = 0; i < 20; i++)
        {
            _limiter.Check(Chat);
        }

        _clock.Advance(TimeSpan.FromSeconds(59));

        _limiter.Check(Chat).ShouldBe(RateVerdict.Throttled);
    }
}
