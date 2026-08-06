using System.Security.Cryptography;
using System.Text;
using JobHunter.Domain.Reporting;
using Microsoft.Extensions.Options;

namespace JobHunter.Telegram.Callbacks;

/// <summary>
/// The signed short id that lets a card survive Telegram's 64-byte <c>callback_data</c> limit
/// ([[../contracts/telegram-messages|contract]] §Callback payloads). A card key encodes to
/// <c>base64url(HMAC-SHA256(cardKey, botSecret)[0..8])</c> — 11 characters — and resolves back only among
/// the candidate keys of a real digest. The HMAC is the point: a payload cannot be forged by guessing a
/// card key, and a short id signed under a different secret does not resolve. The bot secret is a config
/// value that never appears in a log, an exception message or a span (invariant 12), so this type keeps it
/// inside itself and exposes only encode/resolve.
/// </summary>
internal sealed class CallbackDataCodec
{
    // Nine bytes so the payload never carries a full digest; the first eight are the signature and the
    // truncation is deliberate — a short id, not a MAC anyone verifies elsewhere.
    private const int SignatureBytes = 8;

    private readonly byte[] _secret;

    public CallbackDataCodec(IOptions<TelegramOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _secret = Encoding.UTF8.GetBytes(options.Value.BotToken);
    }

    /// <summary>
    /// The 11-character base64url short id for <paramref name="cardKey"/> under the bot secret. Deterministic:
    /// the same key and secret always produce the same id, which is what lets a callback resolve back to a card.
    /// </summary>
    public string Encode(CardKey cardKey)
    {
        ArgumentNullException.ThrowIfNull(cardKey);
        return SignatureOf(cardKey.Value);
    }

    /// <summary>
    /// The candidate whose signed short id equals <paramref name="shortId"/>, or <c>null</c> when none matches —
    /// a forged, unparseable or wrong-secret id, or an id whose card is no longer among the candidates. Never
    /// throws: an unresolvable id is a value the caller turns into a plain message, not an exception (AC-09).
    /// </summary>
    public CardKey? Resolve(string? shortId, IReadOnlyCollection<CardKey> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (string.IsNullOrEmpty(shortId))
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(SignatureOf(candidate.Value)),
                    Encoding.ASCII.GetBytes(shortId)))
            {
                return candidate;
            }
        }

        return null;
    }

    private string SignatureOf(string cardKeyValue)
    {
        var mac = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(cardKeyValue));
        return Base64UrlEncode(mac.AsSpan(0, SignatureBytes));
    }

    // base64url without padding: 8 bytes -> 11 characters, URL- and callback_data-safe.
    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
