using System.Security.Cryptography;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Derives the candidate job id deterministically from the origin raw posting id, so replaying the same
/// <see cref="Contracts.Pipeline.RawPostingIngested"/> always proposes the same job id (SAD §6.1, the
/// idempotency-on-raw-posting-id property). The deduplication stage uses this id when a fingerprint is
/// genuinely new; on a conflict it is discarded in favour of the existing canonical job's id. Deriving it
/// here — rather than minting a fresh UUID per message — is what lets a redelivered ingest converge on the
/// same insert instead of racing itself into a second job.
///
/// <para>Pure and I/O-free: a name-based (RFC 4122 v5-style) UUID over the raw posting id's bytes under a
/// fixed namespace. No clock, no randomness — the same raw posting id yields a byte-identical id on every
/// machine, forever.</para>
/// </summary>
public static class CandidateJobId
{
    // A fixed, arbitrary namespace so the derivation cannot collide with an unrelated name-based UUID scheme.
    private static readonly byte[] Namespace =
        new Guid("6f2b1c9a-3d4e-4f7a-8b12-0a9e5c7d1f30").ToByteArray();

    /// <summary>
    /// Computes the deterministic candidate job id for <paramref name="rawPostingId"/>. Never throws.
    /// </summary>
    public static Guid For(Guid rawPostingId)
    {
        var name = rawPostingId.ToByteArray();
        var input = new byte[Namespace.Length + name.Length];
        Namespace.CopyTo(input, 0);
        name.CopyTo(input, Namespace.Length);

        var hash = SHA256.HashData(input);

        // Take the first 16 bytes and stamp version 5 / RFC-4122 variant bits so the value is a well-formed UUID.
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}
