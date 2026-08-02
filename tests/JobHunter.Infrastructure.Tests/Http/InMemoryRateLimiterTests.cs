using JobHunter.Infrastructure.Http;
using JobHunter.TestKit;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

public sealed class InMemoryRateLimiterTests
{
    private static InMemoryRateLimiter New(FakeClock clock, int rate = 1) =>
        new(clock, Options.Create(new PolitenessOptions { DefaultRequestsPerSecond = rate }));

    [Fact]
    public async Task First_acquire_for_a_host_is_granted()
    {
        var limiter = New(new FakeClock());

        var lease = await limiter.AcquireAsync("boards.example");

        lease.Granted.ShouldBeTrue();
    }

    [Fact]
    public async Task Second_immediate_acquire_is_deferred()
    {
        var clock = new FakeClock();
        var limiter = New(clock);
        await limiter.AcquireAsync("boards.example");

        var lease = await limiter.AcquireAsync("boards.example");

        lease.Granted.ShouldBeFalse();
        lease.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Different_hosts_have_independent_budgets()
    {
        var limiter = New(new FakeClock());
        await limiter.AcquireAsync("a.example");

        var lease = await limiter.AcquireAsync("b.example");

        lease.Granted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_token_refills_after_the_clock_advances()
    {
        var clock = new FakeClock();
        var limiter = New(clock);
        await limiter.AcquireAsync("boards.example");

        clock.Advance(TimeSpan.FromSeconds(1));
        var lease = await limiter.AcquireAsync("boards.example");

        lease.Granted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_penalty_blocks_the_host_for_the_full_duration()
    {
        var clock = new FakeClock();
        var limiter = New(clock);

        await limiter.PenaliseAsync("boards.example", TimeSpan.FromSeconds(120));
        var lease = await limiter.AcquireAsync("boards.example");

        lease.Granted.ShouldBeFalse();
        lease.RetryAfter.ShouldBe(TimeSpan.FromSeconds(120), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task A_penalty_is_never_shortened_by_a_later_shorter_one()
    {
        var clock = new FakeClock();
        var limiter = New(clock);

        await limiter.PenaliseAsync("boards.example", TimeSpan.FromSeconds(120));
        await limiter.PenaliseAsync("boards.example", TimeSpan.FromSeconds(5));

        var lease = await limiter.AcquireAsync("boards.example");
        lease.RetryAfter.ShouldBeGreaterThan(TimeSpan.FromSeconds(100));
    }

    [Fact]
    public async Task A_penalty_expires_once_its_window_passes()
    {
        var clock = new FakeClock();
        var limiter = New(clock);
        await limiter.PenaliseAsync("boards.example", TimeSpan.FromSeconds(120));

        clock.Advance(TimeSpan.FromSeconds(121));
        var lease = await limiter.AcquireAsync("boards.example");

        lease.Granted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_non_positive_penalty_is_a_no_op()
    {
        var clock = new FakeClock();
        var limiter = New(clock);

        await limiter.PenaliseAsync("boards.example", TimeSpan.Zero);
        var lease = await limiter.AcquireAsync("boards.example");

        lease.Granted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_blank_host_is_rejected()
    {
        var limiter = New(new FakeClock());

        await Should.ThrowAsync<ArgumentException>(() => limiter.AcquireAsync(" "));
    }
}
