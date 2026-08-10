using JobHunter.Domain.Commands;
using JobHunter.Infrastructure.Commands;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Commands;

/// <summary>
/// The single-process <see cref="IConversationStateStore"/> that stands in for Redis when no cache is
/// configured (local dev, per <c>ConnectionStringOptions.Cache</c> — the same Redis-optional split the rate
/// limiter follows). It must honour the store's load-bearing contract itself: Redis's native TTL removes an
/// expired document so a returned state is by construction live, and with no TTL here the store applies the
/// same rule against <see cref="IClock"/> — a state read past its <see cref="ConversationState.Lifetime"/>
/// reads back as <c>null</c>, indistinguishable from nothing pending.
/// </summary>
public sealed class InMemoryConversationStateStoreTests
{
    private const long Chat = 4242;

    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_stored_state_is_read_back_with_its_command_awaited_argument_and_context()
    {
        var clock = new FakeClock(Now);
        var store = new InMemoryConversationStateStore(clock);
        var state = new ConversationState(
            "note", "text", new Dictionary<string, string> { ["jobId"] = "0192f8a1" }, clock.UtcNow);

        await store.SetAsync(Chat, state);
        var read = await store.GetAsync(Chat);

        read.ShouldNotBeNull();
        read.Command.ShouldBe("note");
        read.Awaiting.ShouldBe("text");
        read.Context["jobId"].ShouldBe("0192f8a1");
        read.StartedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Nothing_pending_reads_back_as_null()
    {
        var store = new InMemoryConversationStateStore(new FakeClock(Now));

        (await store.GetAsync(Chat)).ShouldBeNull();
    }

    [Fact]
    public async Task Clearing_removes_a_pending_state()
    {
        var clock = new FakeClock(Now);
        var store = new InMemoryConversationStateStore(clock);
        await store.SetAsync(Chat, new ConversationState("note", "text", null, clock.UtcNow));

        await store.ClearAsync(Chat);

        (await store.GetAsync(Chat)).ShouldBeNull();
    }

    [Fact]
    public async Task Clearing_when_nothing_is_pending_is_a_no_op()
    {
        var store = new InMemoryConversationStateStore(new FakeClock(Now));

        await Should.NotThrowAsync(store.ClearAsync(Chat));
    }

    [Fact]
    public async Task Different_chats_hold_independent_state()
    {
        var clock = new FakeClock(Now);
        var store = new InMemoryConversationStateStore(clock);
        await store.SetAsync(Chat, new ConversationState("note", "text", null, clock.UtcNow));

        (await store.GetAsync(Chat + 1)).ShouldBeNull();
    }

    [Fact]
    public async Task A_state_read_past_its_lifetime_reads_back_as_null()
    {
        // No Redis TTL to remove it, so the store applies the lifetime rule itself: a state whose window has
        // elapsed by the time it is read is indistinguishable from nothing pending, exactly as the contract
        // requires (a returned state is by construction live).
        var clock = new FakeClock(Now);
        var store = new InMemoryConversationStateStore(clock);
        await store.SetAsync(Chat, new ConversationState("note", "text", null, clock.UtcNow));

        clock.Advance(ConversationState.Lifetime);

        (await store.GetAsync(Chat)).ShouldBeNull();
    }

    [Fact]
    public async Task A_state_read_within_its_lifetime_is_still_live()
    {
        var clock = new FakeClock(Now);
        var store = new InMemoryConversationStateStore(clock);
        await store.SetAsync(Chat, new ConversationState("note", "text", null, clock.UtcNow));

        clock.Advance(ConversationState.Lifetime - TimeSpan.FromSeconds(1));

        (await store.GetAsync(Chat)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_null_state_is_rejected()
    {
        var store = new InMemoryConversationStateStore(new FakeClock(Now));

        await Should.ThrowAsync<ArgumentNullException>(() => store.SetAsync(Chat, null!));
    }

    [Fact]
    public void A_null_clock_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new InMemoryConversationStateStore(null!));
    }
}
