namespace JobHunter.Domain.Postings;

/// <summary>
/// A flat read model of a stored raw posting's normalisable content (data-model §raw_postings). Returned by
/// the read side for F2 normalisation and reprocessing: the verbatim <see cref="Payload"/> to parse, and the
/// timestamps that become the job's first/last seen. Read-only — F2 never writes <c>raw_postings</c> except
/// the whitelisted retention prune (SAD §2). The full aggregate is never loaded; normalisation needs only
/// these fields.
/// </summary>
public sealed record RawPostingContent(
    Guid Id,
    Guid SourceId,
    string ExternalId,
    string Payload,
    DateTimeOffset FetchedAt,
    DateTimeOffset LastSeenAt);
