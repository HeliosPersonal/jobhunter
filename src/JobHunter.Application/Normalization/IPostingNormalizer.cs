using JobHunter.Domain.Companies;
using JobHunter.Domain.Common;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Provider-specific field extraction: one implementation per <see cref="AtsKind"/> (SAD §5). It reads a
/// raw provider payload — a single posting's verbatim JSON (a JSON-LD node for career pages) — and yields
/// the provider-agnostic <see cref="ExtractedPosting"/> the shared normalisers then consume. Everything
/// downstream of this is provider-agnostic, which is what keeps five providers from becoming five copies of
/// the same title logic.
///
/// <para>Extraction is a <strong>pure function of the payload</strong> (SAD S5): no clock, no randomness,
/// no I/O — which is exactly what makes reprocessing (QG-3) free. A malformed payload or a missing required
/// field is a <see cref="Result{T}"/> failure, never an exception, so one bad posting never halts a batch
/// (AC-04).</para>
/// </summary>
public interface IPostingNormalizer
{
    /// <summary>The provider this normaliser extracts for.</summary>
    AtsKind Kind { get; }

    /// <summary>
    /// Extracts the canonical fields from <paramref name="payload"/> — one posting's verbatim JSON. Returns
    /// a failure (never throws) when the payload is malformed or a required field (title, apply URL) is
    /// absent; the handler records the failure and continues with the rest of the batch (AC-04).
    /// </summary>
    Result<ExtractedPosting> Extract(string payload);
}
