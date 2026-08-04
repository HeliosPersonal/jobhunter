using System.Security.Cryptography;
using System.Text;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Reporting;

/// <summary>
/// The idempotence key of a delivered message (data-model §digest_cards, [[adr/0002-delivery-idempotence|ADR-F5-0002]]).
/// For a job card it is <c>sha256(run_id ‖ job_id)</c> truncated to 16 lowercase hex characters — a pure
/// function of <c>(run_id, job_id)</c>, so a resumed delivery recomputes the same key and can ask "which
/// of these have I already sent" without any coordination. Determinism is the whole point: the same inputs
/// produce the same key across processes and releases.
///
/// <para>The header and footer are not job cards but must go through the same delivery-log mechanism, so
/// they use the reserved keys <see cref="Header"/> and <see cref="Footer"/> rather than a special case that
/// will eventually be got wrong (ADR-F5-0002 detail 2).</para>
/// </summary>
public sealed class CardKey : ValueObject
{
    /// <summary>The reserved key for the digest header message.</summary>
    public const string HeaderValue = "__header__";

    /// <summary>The reserved key for the digest footer message.</summary>
    public const string FooterValue = "__footer__";

    private const int HexLength = 16;

    public static readonly Error Invalid =
        new("digest.card_key.invalid", "A card key must be 16 lowercase hex characters or a reserved key.");

    /// <summary>The reserved header key, so the header is idempotent through the same path as a card.</summary>
    public static readonly CardKey Header = new(HeaderValue);

    /// <summary>The reserved footer key, so the footer is idempotent through the same path as a card.</summary>
    public static readonly CardKey Footer = new(FooterValue);

    private CardKey(string value) => Value = value;

    /// <summary>The 16-character lowercase hex digest, or a reserved key.</summary>
    public string Value { get; }

    /// <summary>True for the header and footer keys, which are not backed by a job.</summary>
    public bool IsReserved => Value is HeaderValue or FooterValue;

    /// <summary>
    /// The deterministic key for a job's card in a Run. Stable across processes and releases: the same
    /// <paramref name="runId"/> and <paramref name="jobId"/> always yield the same key, which is what makes
    /// resumed delivery able to skip what it already sent.
    /// </summary>
    public static CardKey For(Guid runId, Guid jobId)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A card key must reference a Run.", nameof(runId));
        }

        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A card key must reference a Job.", nameof(jobId));
        }

        // The "N" form (32 hex, no dashes) is an unambiguous, platform-stable rendering — unlike the raw
        // Guid bytes, whose mixed-endian layout would make the key depend on the runtime.
        var payload = runId.ToString("N") + jobId.ToString("N");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return new CardKey(Convert.ToHexStringLower(digest)[..HexLength]);
    }

    /// <summary>Rehydrates a key from stored text, validating shape. Never throws — an invalid key is a value.</summary>
    public static Result<CardKey> TryCreate(string? value)
    {
        if (value is HeaderValue or FooterValue)
        {
            return Result<CardKey>.Success(new CardKey(value));
        }

        if (value is null || value.Length != HexLength || !IsLowerHex(value))
        {
            return Invalid;
        }

        return Result<CardKey>.Success(new CardKey(value));
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    private static bool IsLowerHex(string value)
    {
        foreach (var c in value)
        {
            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
