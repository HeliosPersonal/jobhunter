using JobHunter.Domain.Intelligence;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Turns one batch result item's raw tool-use JSON into a validated <see cref="Enrichment"/> — or a
/// recorded reason it could not (enrichment-schema §Parsing rules, T12). The port lives in Domain so the
/// Application result-processing handler depends on it rather than on <c>JobHunter.Claude</c>, where the
/// tolerant parser and the wire DTO shape live; the implementation there applies the eight parsing steps
/// and maps the parsed output onto the domain aggregate (mirroring <see cref="IEnrichmentRequestBuilder"/>).
///
/// <para>Parsing <strong>never throws for a single item</strong>: one malformed result is one recorded
/// failure, not a failed batch (QG-3). A successful outcome is guaranteed to carry at least one reason —
/// the tolerant parser rejects an empty <c>reasons</c> before an <see cref="Enrichment"/> is ever
/// constructed, so invariant 4 holds by construction (AC-02).</para>
/// </summary>
public interface IEnrichmentResultParser
{
    /// <summary>
    /// Parses <paramref name="request"/>'s raw JSON into an <see cref="Enrichment"/> stamped with its
    /// identity, run, prompt version and creation instant, or returns a failure carrying a one-line reason
    /// and the anomalies repaired on the way. A blank payload is itself a failure.
    /// </summary>
    EnrichmentParseOutcome Parse(EnrichmentParseRequest request);
}

/// <summary>
/// The identity and stamping a parsed result needs to become an <see cref="Enrichment"/> aggregate. The
/// handler owns these — the id from <see cref="IIdGenerator"/>, the instant from <see cref="IClock"/> — so
/// the parser stays pure and the result is deterministic under test.
/// </summary>
public sealed record EnrichmentParseRequest(
    Guid EnrichmentId,
    Guid JobId,
    Guid RunId,
    string PromptVersion,
    DateTimeOffset CreatedAt,
    string? RawJson);

/// <summary>
/// The outcome of parsing one item: exactly one of <see cref="Enrichment"/> (parsed and validated) or
/// <see cref="FailureReason"/> (recorded <c>ParseFailed</c>, raw retained) is present. Repairs applied on
/// the way to success — a swapped salary range, a clamped confidence, a degraded enum — are listed in
/// <see cref="Anomalies"/> so a suspiciously poor assessment stays inspectable.
/// </summary>
public sealed class EnrichmentParseOutcome
{
    private EnrichmentParseOutcome(Enrichment? enrichment, string? failureReason, IReadOnlyList<string> anomalies)
    {
        Enrichment = enrichment;
        FailureReason = failureReason;
        Anomalies = anomalies;
    }

    public Enrichment? Enrichment { get; }

    public string? FailureReason { get; }

    public bool IsSuccess => Enrichment is not null;

    public IReadOnlyList<string> Anomalies { get; }

    public static EnrichmentParseOutcome Success(Enrichment enrichment, IReadOnlyList<string>? anomalies = null)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        return new EnrichmentParseOutcome(enrichment, null, anomalies ?? []);
    }

    public static EnrichmentParseOutcome Failure(string reason) =>
        new(null, string.IsNullOrWhiteSpace(reason) ? "unspecified parse failure" : reason, []);
}
