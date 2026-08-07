using JobHunter.Domain.Commands;
using JobHunter.Infrastructure.Commands;
using JobHunter.TestKit;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Commands;

/// <summary>
/// The one place the real Redis conversation-state path is exercised (test-plan: the real Redis path is
/// covered once, in an integration test). It asserts the round trip, the native TTL that <em>is</em> the
/// expiry mechanism (data-model §Conversation state), clearing, and — the load-bearing contract — that a
/// Redis outage degrades to "nothing pending" rather than faulting, so a read command is never taken
/// down by the store. Requires Docker; skipped cleanly where none is reachable.
/// </summary>
public sealed class RedisConversationStateStoreTests
{
    private const long Chat = 4242;

    [RequiresDockerFact]
    public async Task A_stored_state_is_read_back_with_its_command_awaited_argument_and_context()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var store = New(redis.Multiplexer, clock);
        var state = new ConversationState(
            "note", "text", new Dictionary<string, string> { ["applicationId"] = "0192f8a1" }, clock.UtcNow);

        await store.SetAsync(Chat, state);
        var read = await store.GetAsync(Chat);

        read.ShouldNotBeNull();
        read.Command.ShouldBe("note");
        read.Awaiting.ShouldBe("text");
        read.Context["applicationId"].ShouldBe("0192f8a1");
        read.StartedAt.ShouldBe(clock.UtcNow);
    }

    [RequiresDockerFact]
    public async Task Nothing_pending_reads_back_as_null()
    {
        await using var redis = await StartAsync();
        var store = New(redis.Multiplexer, new FakeClock());

        (await store.GetAsync(Chat)).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task A_stored_state_carries_a_ttl_at_most_its_five_minute_lifetime()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var store = New(redis.Multiplexer, clock);
        await store.SetAsync(Chat, new ConversationState("note", "text", null, clock.UtcNow));

        var ttl = await redis.Multiplexer.GetDatabase().KeyTimeToLiveAsync(KeyFor(Chat));

        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        ttl.Value.ShouldBeLessThanOrEqualTo(ConversationState.Lifetime);
    }

    [RequiresDockerFact]
    public async Task Clearing_removes_a_pending_state()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var store = New(redis.Multiplexer, clock);
        await store.SetAsync(Chat, new ConversationState("note", "text", null, clock.UtcNow));

        await store.ClearAsync(Chat);

        (await store.GetAsync(Chat)).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task Clearing_when_nothing_is_pending_is_a_no_op()
    {
        await using var redis = await StartAsync();
        var store = New(redis.Multiplexer, new FakeClock());

        await Should.NotThrowAsync(store.ClearAsync(Chat));
    }

    [RequiresDockerFact]
    public async Task Different_chats_hold_independent_state()
    {
        await using var redis = await StartAsync();
        var clock = new FakeClock();
        var store = New(redis.Multiplexer, clock);
        await store.SetAsync(Chat, new ConversationState("note", "text", null, clock.UtcNow));

        (await store.GetAsync(Chat + 1)).ShouldBeNull();
    }

    [Fact]
    public async Task A_get_against_an_unreachable_store_degrades_to_null_rather_than_faulting()
    {
        var store = New(BrokenMultiplexer(), new FakeClock());

        (await store.GetAsync(Chat)).ShouldBeNull();
    }

    [Fact]
    public async Task A_set_against_an_unreachable_store_is_swallowed()
    {
        var store = New(BrokenMultiplexer(), new FakeClock());

        await Should.NotThrowAsync(store.SetAsync(
            Chat, new ConversationState("note", "text", null, DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public async Task A_clear_against_an_unreachable_store_is_swallowed()
    {
        var store = New(BrokenMultiplexer(), new FakeClock());

        await Should.NotThrowAsync(store.ClearAsync(Chat));
    }

    private static RedisConversationStateStore New(IConnectionMultiplexer multiplexer, FakeClock clock) =>
        new(multiplexer, clock);

    private static RedisKey KeyFor(long chatId) => $"jobhunter:convstate:{chatId}";

    private static ConnectionMultiplexer BrokenMultiplexer()
    {
        // A multiplexer that cannot connect: every command it hands out throws, which is exactly the outage
        // the store must degrade through rather than surface to the caller.
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
