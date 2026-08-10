using System.Collections.Concurrent;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;

namespace JobHunter.Infrastructure.Commands;

/// <summary>
/// The single-process <see cref="IConversationStateStore"/> that stands in for
/// <see cref="RedisConversationStateStore"/> when no cache is configured — the same Redis-optional split the
/// rate limiter follows (<c>ConnectionStringOptions.Cache</c>), so a local <c>dotnet run</c> keeps the
/// multi-step commands working without a Redis dependency.
///
/// <para>The Redis store leans on the native TTL to remove an expired document, so a state it returns is by
/// construction live. There is no TTL here, so this store enforces the same rule itself: a state read at or
/// past its <see cref="ConversationState.Lifetime"/> is dropped and reads back as <c>null</c>, indistinguishable
/// from nothing pending — exactly the degraded shape the contract requires. Being single-process it has no
/// outage to swallow; the best-effort contract is satisfied trivially.</para>
/// </summary>
internal sealed class InMemoryConversationStateStore(IClock clock) : IConversationStateStore
{
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ConcurrentDictionary<long, ConversationState> _states = new();

    public Task<ConversationState?> GetAsync(long chatId, CancellationToken cancellationToken = default)
    {
        if (!_states.TryGetValue(chatId, out var state))
        {
            return Task.FromResult<ConversationState?>(null);
        }

        // No TTL sweeper stands in for Redis here, so the lifetime is enforced on read: an expired state is
        // removed and reported as nothing pending, keeping the "a returned state is by construction live"
        // contract that the resolver relies on.
        if (state.HasExpired(_clock.UtcNow, ConversationState.Lifetime))
        {
            _states.TryRemove(chatId, out _);
            return Task.FromResult<ConversationState?>(null);
        }

        return Task.FromResult<ConversationState?>(state);
    }

    public Task SetAsync(long chatId, ConversationState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        _states[chatId] = state;
        return Task.CompletedTask;
    }

    public Task ClearAsync(long chatId, CancellationToken cancellationToken = default)
    {
        _states.TryRemove(chatId, out _);
        return Task.CompletedTask;
    }
}
