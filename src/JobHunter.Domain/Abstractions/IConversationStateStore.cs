using JobHunter.Domain.Commands;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The per-chat store of a pending multi-step conversation (SAD §6.2, data-model §Conversation state).
/// Its one implementation is Redis-backed under <c>{env}:jobhunter:convstate:{chat_id}</c> with a native
/// 300-second TTL, chosen precisely so the expiry <em>is</em> the store — there is no sweeper to fail and
/// leave a chat wedged, and a pod restart cannot wedge one either.
///
/// <para>The store is best-effort by contract: a Redis outage must degrade multi-step commands to
/// requiring their argument inline, never fault a read command. So <see cref="GetAsync"/> returns
/// <c>null</c> both when nothing is pending and when the store is unreachable — the caller treats the
/// two the same — and <see cref="SetAsync"/> and <see cref="ClearAsync"/> swallow an outage rather than
/// surface it. The store never decides whether a state has expired; Redis's TTL removes it, so a
/// returned state is by construction live.</para>
/// </summary>
public interface IConversationStateStore
{
    /// <summary>The pending state for <paramref name="chatId"/>, or <c>null</c> if none — or the store is down.</summary>
    Task<ConversationState?> GetAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="state"/> for <paramref name="chatId"/> under the lifetime TTL.</summary>
    Task SetAsync(long chatId, ConversationState state, CancellationToken cancellationToken = default);

    /// <summary>Clears any pending state for <paramref name="chatId"/>; a no-op when nothing is pending.</summary>
    Task ClearAsync(long chatId, CancellationToken cancellationToken = default);
}
