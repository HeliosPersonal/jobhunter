using JobHunter.Domain.Commands;
using JobHunter.Infrastructure.Commands;
using JobHunter.TestKit;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Commands;

/// <summary>
/// The one place the real Redis confirmation path runs (test-plan: nonce issue, validate, burn, expire,
/// replay). It asserts the round trip, the native TTL that <em>is</em> the two-minute expiry, and — the
/// load-bearing contract — that redemption is atomically single-use: a second tap of the same nonce sees a
/// used token, never a second confirmation. It also proves the fail-closed rule: a confirmation store
/// outage never silently lets a state-changing command through. Requires Docker; skipped cleanly where none
/// is reachable.
/// </summary>
public sealed class RedisConfirmationStoreTests
{
    private const long Chat = 4242;

    private static ConfirmationToken Token(string nonce, FakeClock clock) =>
        new(nonce, Chat, "run", "2026-08", clock.UtcNow);

    [RequiresDockerFact]
    public async Task An_issued_token_redeems_once_carrying_its_command_and_arguments()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var store = New(redis.Multiplexer, clock);
        await store.IssueAsync(Token("n1", clock));

        var redeemed = await store.RedeemAsync("n1");

        redeemed.ShouldNotBeNull();
        redeemed.Nonce.ShouldBe("n1");
        redeemed.ChatId.ShouldBe(Chat);
        redeemed.Command.ShouldBe("run");
        redeemed.ArgumentTail.ShouldBe("2026-08");
        redeemed.Used.ShouldBeFalse();
    }

    [RequiresDockerFact]
    public async Task A_second_redemption_of_the_same_nonce_returns_a_used_token()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var store = New(redis.Multiplexer, clock);
        await store.IssueAsync(Token("n1", clock));

        var first = await store.RedeemAsync("n1");
        var second = await store.RedeemAsync("n1");

        first!.Used.ShouldBeFalse();
        second.ShouldNotBeNull();
        second.Used.ShouldBeTrue();
    }

    [RequiresDockerFact]
    public async Task A_nonce_that_was_never_issued_redeems_as_null()
    {
        await using var redis = await StartAsync();
        var store = New(redis.Multiplexer, new FakeClock());

        (await store.RedeemAsync("never")).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task An_issued_token_carries_a_ttl_at_most_its_two_minute_lifetime()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var store = New(redis.Multiplexer, clock);
        await store.IssueAsync(Token("n1", clock));

        var ttl = await redis.Multiplexer.GetDatabase().KeyTimeToLiveAsync(KeyFor("n1"));

        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        ttl.Value.ShouldBeLessThanOrEqualTo(ConfirmationToken.Lifetime);
    }

    [RequiresDockerFact]
    public async Task Distinct_nonces_redeem_independently()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var store = New(redis.Multiplexer, clock);
        await store.IssueAsync(Token("n1", clock));
        await store.IssueAsync(Token("n2", clock));

        await store.RedeemAsync("n1");
        var other = await store.RedeemAsync("n2");

        other.ShouldNotBeNull();
        other.Used.ShouldBeFalse();
    }

    [RequiresDockerFact]
    public async Task An_already_expired_token_is_never_stored_and_redeems_as_null()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var store = New(redis.Multiplexer, clock);
        // Issued two minutes ago: no remaining lifetime, so nothing is written and there is nothing to redeem.
        var stale = new ConfirmationToken("n1", Chat, "run", "", clock.UtcNow - ConfirmationToken.Lifetime);

        await store.IssueAsync(stale);

        (await store.RedeemAsync("n1")).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task A_corrupt_stored_document_redeems_as_null_rather_than_faulting()
    {
        await using var redis = await StartAsync();
        var store = New(redis.Multiplexer, new FakeClock());
        // A value that deserialises to null is treated as no token, not an exception.
        await redis.Multiplexer.GetDatabase().StringSetAsync(KeyFor("n1"), "null", ConfirmationToken.Lifetime);

        (await store.RedeemAsync("n1")).ShouldBeNull();
    }

    [Fact]
    public async Task Redeeming_against_an_unreachable_store_returns_null_so_the_command_is_refused()
    {
        var store = New(BrokenMultiplexer(), new FakeClock());

        (await store.RedeemAsync("n1")).ShouldBeNull();
    }

    [Fact]
    public async Task Issuing_against_an_unreachable_store_fails_closed_rather_than_silently()
    {
        var clock = new FakeClock();
        var store = New(BrokenMultiplexer(), clock);

        await Should.ThrowAsync<RedisException>(store.IssueAsync(Token("n1", clock)));
    }

    private static RedisConfirmationStore New(IConnectionMultiplexer multiplexer, FakeClock clock) =>
        new(multiplexer, clock);

    private static RedisKey KeyFor(string nonce) => $"jobhunter:confirm:{nonce}";

    private static ConnectionMultiplexer BrokenMultiplexer()
    {
        // A multiplexer that cannot connect: every command it hands out throws, which for a confirmation
        // must fail closed on issue and read as "no token" on redeem — never a silent unconfirmed run.
        var config = new ConfigurationOptions
        {
            EndPoints = { { "127.0.0.1", 6399 } },
            AbortOnConnectFail = false,
            ConnectTimeout = 200,
            ConnectRetry = 0,
        };
        return ConnectionMultiplexer.Connect(config);
    }

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
        public ConnectionMultiplexer Multiplexer => multiplexer;

        public async ValueTask DisposeAsync()
        {
            await multiplexer.DisposeAsync();
            await container.DisposeAsync();
        }
    }
}
