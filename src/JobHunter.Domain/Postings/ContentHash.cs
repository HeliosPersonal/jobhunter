using System.Security.Cryptography;
using System.Text;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Postings;

/// <summary>
/// A SHA-256 digest of a posting's payload after volatile fields are stripped (data-model §raw_postings
/// <c>content_hash char(64)</c>). It is the "did the content actually change?" key: two fetches whose
/// only difference is an <c>updated_at</c> timestamp produce the same hash, so a re-fetch bumps
/// <c>last_seen_at</c> rather than creating a new row (AC-02). Rendered as 64 lowercase hex characters.
/// </summary>
public sealed class ContentHash : ValueObject
{
    private const int HexLength = 64;

    public static readonly Error Invalid =
        new("posting.content_hash.invalid", "A content hash must be 64 lowercase hex characters.");

    private ContentHash(string value) => Value = value;

    /// <summary>The 64-character lowercase hex digest.</summary>
    public string Value { get; }

    /// <summary>Computes the hash of already-canonicalised bytes (the adapter strips volatile fields first).</summary>
    public static ContentHash Compute(string canonicalPayload)
    {
        ArgumentNullException.ThrowIfNull(canonicalPayload);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload));
        return new ContentHash(Convert.ToHexStringLower(digest));
    }

    /// <summary>Rehydrates a hash from stored text, validating shape. Never throws.</summary>
    public static Result<ContentHash> TryCreate(string? value)
    {
        if (value is null || value.Length != HexLength || !IsLowerHex(value))
        {
            return Invalid;
        }

        return Result<ContentHash>.Success(new ContentHash(value));
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
