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

    // A GUID is 16 bytes; a rating payload carries the job id in full plus its 8-byte signature.
    private const int GuidBytes = 16;

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

    /// <summary>
    /// The self-contained rating payload for <paramref name="jobId"/> (F4 T20): the 16 job-id bytes followed by
    /// an 8-byte HMAC over them, base64url-encoded to 32 characters. Unlike <see cref="Encode(CardKey)"/> it
    /// carries the identity <em>inside</em> the payload, so a weekly rating tap resolves from the payload alone —
    /// no candidate lookup and no time window, which is the point: the Owner may rate a card up to a week old and
    /// the tap must never fall out of a sliding resolution window and silently lose the <c>Rated</c> signal.
    /// </summary>
    public string EncodeRating(Guid jobId)
    {
        Span<byte> payload = stackalloc byte[GuidBytes + SignatureBytes];
        jobId.TryWriteBytes(payload[..GuidBytes]);
        RatingSignature(payload[..GuidBytes]).CopyTo(payload[GuidBytes..]);
        return Base64UrlEncode(payload);
    }

    /// <summary>
    /// The job id a rating payload carries, or <c>null</c> when the payload is unparseable, the wrong length, or
    /// its signature does not verify under the bot secret — a forged or tampered rating resolves to nothing.
    /// Never throws: an unresolvable payload is a value the caller turns into a plain acknowledgement (AC-09).
    /// </summary>
    public Guid? ResolveRating(string? payload)
    {
        if (string.IsNullOrEmpty(payload) || !TryBase64UrlDecode(payload, out var bytes) ||
            bytes.Length != GuidBytes + SignatureBytes)
        {
            return null;
        }

        var jobIdBytes = bytes.AsSpan(0, GuidBytes);
        if (!CryptographicOperations.FixedTimeEquals(RatingSignature(jobIdBytes), bytes.AsSpan(GuidBytes)))
        {
            return null;
        }

        return new Guid(jobIdBytes);
    }

    private string SignatureOf(string cardKeyValue)
    {
        var mac = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(cardKeyValue));
        return Base64UrlEncode(mac.AsSpan(0, SignatureBytes));
    }

    // The 8-byte truncated HMAC over the raw job-id bytes — the guard that stops a rating being forged for a
    // job that was never prompted, or an existing payload being pointed at a different job.
    private byte[] RatingSignature(ReadOnlySpan<byte> jobIdBytes) =>
        HMACSHA256.HashData(_secret, jobIdBytes.ToArray())[..SignatureBytes];

    // base64url without padding: 8 bytes -> 11 characters, URL- and callback_data-safe.
    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // The inverse: re-pad the base64url text and decode it. Returns false for anything that is not valid
    // base64url, so a mangled or truncated payload is a value the caller rejects, never an exception (AC-09).
    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        var standard = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (standard.Length % 4)) % 4;
        Span<byte> buffer = new byte[((standard.Length + padding) / 4) * 3];
        if (Convert.TryFromBase64Chars(standard + new string('=', padding), buffer, out var written))
        {
            bytes = buffer[..written].ToArray();
            return true;
        }

        bytes = [];
        return false;
    }
}
