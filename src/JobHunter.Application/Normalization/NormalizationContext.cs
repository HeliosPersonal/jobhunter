namespace JobHunter.Application.Normalization;

/// <summary>
/// The non-payload facts a candidate job needs that come from the ingest event and the company registry,
/// not the provider payload: which company and raw posting this is, the source that fetched it, the
/// company's canonical domain (a fingerprint input, ADR-F2-0001), and the first/last-seen instants the
/// caller supplies from <c>raw_postings</c>. Kept separate from <see cref="ExtractedPosting"/> so the
/// provider normaliser stays a pure function of the payload alone (SAD S5).
/// </summary>
public sealed record NormalizationContext(
    Guid CompanyId,
    Guid RawPostingId,
    Guid SourceId,
    string CanonicalDomain,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
