using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using StackExchange.Redis;

namespace JobHunter.Infrastructure.Commands;

/// <summary>
/// The Redis-backed <see cref="IConfirmationStore"/> (SAD §6.3, data-model §Conversation state). A pending
/// confirmation lives under <c>{env}:jobhunter:confirm:{nonce}</c> as one small JSON document with a native
/// two-minute TTL — the TTL <em>is</em> the expiry, so a forgotten confirmation cannot replay and no
/// sweeper can fail.
///
/// <para>Single use is enforced atomically. Redemption runs one Lua script that reads the document and, in
/// the same round trip, takes a companion <c>:used</c> marker with <c>SET NX</c>: the first tap takes the
/// marker and reads the token unused, every later tap finds the marker already present and reads it used.
/// Two concurrent taps of the same nonce therefore cannot both confirm — exactly one wins the marker. The
/// document is not deleted on redemption, so a second tap sees a <em>used</em> token (answered "already
/// used") rather than an absent one (answered "expired"); the TTL removes both together.</para>
///
/// <para>Unlike the conversation-state store, a confirmation must <b>fail closed</b>. Issuing does not
/// swallow a Redis outage — it lets the fault propagate, so a state-changing command is never shown a
/// confirmation that was never stored. Redemption against an unreachable store returns <c>null</c>, which
/// the service reports as expired and refuses: no path runs a command unconfirmed.</para>
/// </summary>
internal sealed class RedisConfirmationStore(IConnectionMultiplexer multiplexer, IClock clock)
    : IConfirmationStore
{
    private const string KeyPrefix = "jobhunter:confirm";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // KEYS[1] the document, KEYS[2] the single-use marker. Reads the document and atomically claims the
    // marker: returns {doc, 0} on the first tap (marker newly set) and {doc, 1} on every later tap (marker
    // already held), or an empty result when no document exists. The marker inherits the document's
    // remaining TTL so the two expire together.
    private const string RedeemScript =
        """
        local doc = redis.call('GET', KEYS[1])
        if not doc then
            return {}
        end
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
            ttl = 1
        end
        local first = redis.call('SET', KEYS[2], '1', 'NX', 'PX', ttl)
        if first then
            return {doc, 0}
        end
        return {doc, 1}
        """;

    public async Task IssueAsync(ConfirmationToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var document = new TokenDocument(token.Nonce, token.ChatId, token.Command, token.ArgumentTail, token.IssuedAt);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        // The TTL is the remaining lifetime from now, so a token issued late still expires on the same
        // wall-clock deadline. A non-positive remainder means it is already expired: store nothing.
        var ttl = ConfirmationToken.Lifetime - (clock.UtcNow - token.IssuedAt);
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        // Deliberately not wrapped: a confirmation must fail closed, so a store outage surfaces here rather
        // than showing the Owner a confirmation that was never persisted.
        await multiplexer.GetDatabase().StringSetAsync(Key(token.Nonce), json, ttl).ConfigureAwait(false);
    }

    public async Task<ConfirmationToken?> RedeemAsync(string nonce, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        try
        {
            var result = (RedisResult[]?)await multiplexer.GetDatabase()
                .ScriptEvaluateAsync(RedeemScript, [Key(nonce), UsedKey(nonce)])
                .ConfigureAwait(false);
            if (result is null || result.Length == 0)
            {
                return null;
            }

            var document = JsonSerializer.Deserialize<TokenDocument>((string)result[0]!, JsonOptions);
            if (document is null)
            {
                return null;
            }

            var used = (long)result[1] == 1;
            return new ConfirmationToken(
                document.Nonce, document.ChatId, document.Command, document.ArgumentTail, document.IssuedAt, used);
        }
        catch (RedisException)
        {
            // The store is unreachable: report no token so the confirmation is refused, never run unconfirmed.
            return null;
        }
    }

    private static RedisKey Key(string nonce) => $"{KeyPrefix}:{nonce}";

    private static RedisKey UsedKey(string nonce) => $"{KeyPrefix}:{nonce}:used";

    private sealed record TokenDocument(
        string Nonce,
        long ChatId,
        string Command,
        string ArgumentTail,
        DateTimeOffset IssuedAt);
}
