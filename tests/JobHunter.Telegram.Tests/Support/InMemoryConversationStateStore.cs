using System.Collections.Concurrent;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;

namespace JobHunter.Telegram.Tests.Support;

/// <summary>
/// An in-memory <see cref="IConversationStateStore"/> for the coordinator suite: it keeps the last state set
/// per chat and honours <see cref="ClearAsync"/>, so a test can drive a real Get/Set/Clear interplay rather
/// than script a mock. It does <em>not</em> expire — the pure <see cref="Application.Commands.ConversationTurnResolver"/>
/// owns the expiry decision against the clock, exactly as the Redis TTL owns it in production, so the store
/// deliberately returns whatever was last set (the coordinator suite drives expiry through a stale
/// <see cref="ConversationState.StartedAt"/>, not through this store dropping it).
/// </summary>
public sealed class InMemoryConversationStateStore : IConversationStateStore
{
    private readonly ConcurrentDictionary<long, ConversationState> _states = new();

    public int Clears { get; private set; }

    public void Seed(long chatId, ConversationState state) => _states[chatId] = state;

    public Task<ConversationState?> GetAsync(long chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_states.TryGetValue(chatId, out var state) ? state : null);

    public Task SetAsync(long chatId, ConversationState state, CancellationToken cancellationToken = default)
    {
        _states[chatId] = state;
        return Task.CompletedTask;
    }

    public Task ClearAsync(long chatId, CancellationToken cancellationToken = default)
    {
        _states.TryRemove(chatId, out _);
        Clears++;
        return Task.CompletedTask;
    }
}
