using System.Security.Cryptography;
using System.Text;
using JobHunter.Domain.Common;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Deduplication;

/// <summary>
/// Computes the conservative dedup <see cref="Fingerprint"/> (ADR-F2-0001, SAD §8):
/// <c>sha256(lower(domain) ‖ 0x1f ‖ normalisedTitle ‖ 0x1f ‖ sortedLocationKeys)</c>. The unit-separator
/// byte (<c>0x1f</c>) between fields is what prevents a boundary collision — a domain ending in the text a
/// title begins with can never fingerprint-collide across the join, so <c>acme.com</c>+<c>"x"</c> is
/// distinct from <c>acme.co</c>+<c>"mx"</c>. The inputs are exactly the three ADR-F2-0001 fields and nothing
/// derived from the description, which is too variable to key on.
///
/// <para>Deterministic and culture-invariant by construction (QG-2): the domain is lowercased with the
/// invariant culture, the normalised title arrives already lower-invariant from <see cref="TitleNormalizer"/>,
/// and the location key set is <see cref="LocationSet.SortedKey"/> — ordinal-sorted, already lower-cased.
/// No clock, no randomness, so the same inputs produce a byte-identical digest on every machine, in any
/// culture, forever. The version is a constant here; changing the algorithm is a versioned migration
/// (SAD §11 D3), never a silent redefinition.</para>
/// </summary>
public static class FingerprintCalculator
{
    /// <summary>The current fingerprint algorithm version, stamped on every job (SAD §11 D3).</summary>
    public const short Version = 1;

    private const byte UnitSeparator = 0x1f;

    /// <summary>
    /// Computes the fingerprint from the canonical company <paramref name="domain"/>, the already-normalised
    /// <paramref name="normalisedTitle"/> and the job's <paramref name="locations"/>. The domain is lowered
    /// invariantly; the other two are consumed as given (both are already culture-invariant). Never throws.
    /// </summary>
    public static Fingerprint Compute(string domain, string normalisedTitle, LocationSet locations)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(normalisedTitle);
        ArgumentNullException.ThrowIfNull(locations);

        var buffer = new byte[1];

        using var sha = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);

        WriteUtf8(stream, domain.ToLowerInvariant());
        buffer[0] = UnitSeparator;
        stream.Write(buffer, 0, 1);
        WriteUtf8(stream, normalisedTitle);
        buffer[0] = UnitSeparator;
        stream.Write(buffer, 0, 1);
        WriteUtf8(stream, locations.SortedKey);

        stream.FlushFinalBlock();

        var hex = Convert.ToHexStringLower(sha.Hash!);

        // The digest is 64 lowercase hex chars by construction, so TryCreate always succeeds here; the guard
        // keeps the invariant explicit rather than trusting it implicitly.
        var fingerprint = Fingerprint.TryCreate(hex);
        return fingerprint.IsSuccess
            ? fingerprint.Value
            : throw new InvalidOperationException("SHA-256 produced a non-canonical fingerprint.");
    }

    private static void WriteUtf8(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
