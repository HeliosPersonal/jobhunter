using JobHunter.Domain.Abstractions;
using JobHunter.Infrastructure.Http;
using JobHunter.TestKit;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

/// <summary>
/// The one place the real Redis token-bucket path is exercised (test-plan: "Redis is faked in-memory for
/// units; the real Redis path is covered once, in an integration test"). It asserts the same behaviours
/// the pure <see cref="TokenBucket"/> unit tests do — first take granted, second deferred, refill after a
/// second, a penalty honoured exactly and never shortened — but through the atomic Lua script over a live
/// server. Requires Docker; skipped cleanly where none is reachable.
/// </summary>
public sealed class RedisTokenBucketTests
{
    private static readonly PolitenessOptions Options = new() { DefaultRequestsPerSecond = 1 };

    [RequiresDockerFact]
    public async Task First_take_is_granted_and_the_immediate_second_is_deferred()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var bucket = New(redis.Multiplexer, clock);

        var first = await bucket.AcquireAsync("boards.example");
        var second = await bucket.AcquireAsync("boards.example");

        first.Granted.ShouldBeTrue();
        second.Granted.ShouldBeFalse();
        second.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [RequiresDockerFact]
    public async Task A_token_refills_after_a_second_of_clock_time()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var bucket = New(redis.Multiplexer, clock);
        await bucket.AcquireAsync("boards.example");

        clock.Advance(TimeSpan.FromSeconds(1));
        var third = await bucket.AcquireAsync("boards.example");

        third.Granted.ShouldBeTrue();
    }

    [RequiresDockerFact]
    public async Task Different_hosts_have_independent_budgets()
    {
        await using var redis = await StartAsync();
        var bucket = New(redis.Multiplexer, new FakeClock());
        await bucket.AcquireAsync("a.example");

        var other = await bucket.AcquireAsync("b.example");

        other.Granted.ShouldBeTrue();
    }

    [RequiresDockerFact]
    public async Task A_penalty_blocks_the_host_for_the_full_duration_and_is_never_shortened()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var bucket = New(redis.Multiplexer, clock);

        await bucket.PenaliseAsync("boards.example", TimeSpan.FromSeconds(120));
        await bucket.PenaliseAsync("boards.example", TimeSpan.FromSeconds(5));

        var lease = await bucket.AcquireAsync("boards.example");
        lease.Granted.ShouldBeFalse();
        lease.RetryAfter.ShouldBeGreaterThan(TimeSpan.FromSeconds(100));
    }

    [RequiresDockerFact]
    public async Task A_penalty_expires_once_its_window_passes()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var bucket = New(redis.Multiplexer, clock);
        await bucket.PenaliseAsync("boards.example", TimeSpan.FromSeconds(30));

        clock.Advance(TimeSpan.FromSeconds(31));
        var lease = await bucket.AcquireAsync("boards.example");

        lease.Granted.ShouldBeTrue();
    }

    private static RedisTokenBucket New(IConnectionMultiplexer multiplexer, IClock clock) =>
        new(multiplexer, clock, Microsoft.Extensions.Options.Options.Create(Options));

    private static async Task<RedisFixture> StartAsync()
    {
        var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
        return new RedisFixture(container, multiplexer);
    }

    private sealed class RedisFixture(RedisContainer container, ConnectionMultiplexer multiplexer)
        : IAsyncDisposable
    {
        public IConnectionMultiplexer Multiplexer => multiplexer;

        public async ValueTask DisposeAsync()
        {
            await multiplexer.DisposeAsync();
            await container.DisposeAsync();
        }
    }
}
