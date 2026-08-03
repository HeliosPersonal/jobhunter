using JobHunter.Domain.Common;

namespace JobHunter.Domain.Jobs;

/// <summary>
/// The conservative dedup key: a SHA-256 digest over the canonical domain, the normalised title and the
/// sorted location key set (ADR-F2-0001). It is the uniqueness arbiter — a unique index on this value is
/// what makes two concurrent consumers racing on one opening produce exactly one job with no lock
/// (data-model §jobs). This type only carries and validates the 64-character lowercase hex value; the
/// algorithm that produces it lives in the F2 fingerprint calculator (T06), so a change to the algorithm
/// is a versioned, explicit migration rather than a silent redefinition.
/// </summary>
public sealed class Fingerprint : ValueObject
{
    private const int HexLength = 64;

    public static readonly Error Invalid =
        new("job.fingerprint.invalid", "A fingerprint must be 64 lowercase hex characters.");

    private Fingerprint(string value) => Value = value;

    /// <summary>The 64-character lowercase hex digest.</summary>
    public string Value { get; }

    /// <summary>
    /// Rehydrates a fingerprint from stored or computed text, validating shape. Never throws — a
    /// malformed value is a business outcome (a bad stored row) handled as a failure, not a crash.
    /// </summary>
    public static Result<Fingerprint> TryCreate(string? value)
    {
        if (value is null || value.Length != HexLength || !IsLowerHex(value))
        {
            return Invalid;
        }

        return Result<Fingerprint>.Success(new Fingerprint(value));
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
