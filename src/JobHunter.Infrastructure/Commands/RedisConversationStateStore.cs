using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using StackExchange.Redis;

namespace JobHunter.Infrastructure.Commands;

/// <summary>
/// The Redis-backed <see cref="IConversationStateStore"/> (SAD §6.2, data-model §Conversation state). A
/// pending conversation lives under <c>{env}:jobhunter:convstate:{chat_id}</c> as one small JSON document
/// with a native TTL of <see cref="ConversationState.Lifetime"/> — the TTL <em>is</em> the expiry, so no
/// sweeper can fail and leave a chat wedged, and a pod restart cannot wedge one either.
///
/// <para>The store is best-effort by contract. A Redis outage must degrade multi-step commands to
/// requiring the argument inline and must never take down a read command, so every operation is wrapped:
/// a failed <see cref="GetAsync"/> returns <c>null</c> (indistinguishable from nothing pending, which is
/// the correct degraded behaviour) and a failed set or clear is swallowed. It never decides expiry
/// itself; a document Redis still holds is by construction live.</para>
/// </summary>
internal sealed class RedisConversationStateStore(IConnectionMultiplexer multiplexer, IClock clock)
    : IConversationStateStore
{
    private const string KeyPrefix = "jobhunter:convstate";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ConversationState?> GetAsync(long chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await multiplexer.GetDatabase().StringGetAsync(Key(chatId)).ConfigureAwait(false);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            var document = JsonSerializer.Deserialize<StateDocument>((string)value!, JsonOptions);
            return document is null
                ? null
                : new ConversationState(document.Command, document.Awaiting, document.Context, document.StartedAt);
        }
        catch (RedisException)
        {
            // The store is unreachable: report nothing pending so the command degrades to inline argument.
            return null;
        }
    }

    public async Task SetAsync(long chatId, ConversationState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            var document = new StateDocument(state.Command, state.Awaiting, state.Context, state.StartedAt);
            var json = JsonSerializer.Serialize(document, JsonOptions);
            // The TTL is the remaining lifetime from now, so a state stored late in a step still expires on
            // the same wall-clock deadline rather than resetting the five-minute window.
            var elapsed = clock.UtcNow - state.StartedAt;
            var ttl = ConversationState.Lifetime - elapsed;
            if (ttl <= TimeSpan.Zero)
            {
                return;
            }

            await multiplexer.GetDatabase().StringSetAsync(Key(chatId), json, ttl).ConfigureAwait(false);
        }
        catch (RedisException)
        {
            // A store outage degrades multi-step commands; it is never surfaced to the Owner.
        }
    }

    public async Task ClearAsync(long chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            await multiplexer.GetDatabase().KeyDeleteAsync(Key(chatId)).ConfigureAwait(false);
        }
        catch (RedisException)
        {
            // Nothing to surface: a failed clear is an operational fault, not a failed command.
        }
    }

    private static RedisKey Key(long chatId) => $"{KeyPrefix}:{chatId}";

    private sealed record StateDocument(
        string Command,
        string Awaiting,
        IReadOnlyDictionary<string, string> Context,
        DateTimeOffset StartedAt);
}
