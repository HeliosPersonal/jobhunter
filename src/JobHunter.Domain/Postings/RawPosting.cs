using JobHunter.Domain.Common;

namespace JobHunter.Domain.Postings;

/// <summary>
/// A verbatim posting as fetched from a board, and the highest-volume table in the system
/// (data-model §raw_postings). <strong>Immutable</strong> ([[CONTEXT]] invariant 1): the payload is
/// never edited, and the only field that ever moves after creation is <see cref="LastSeenAt"/>, bumped
/// on an unchanged re-fetch. F2 reads <see cref="Payload"/> to normalise; it never writes here.
/// </summary>
public sealed class RawPosting : Entity
{
    public RawPosting(
        Guid id,
        Guid sourceId,
        string externalId,
        ContentHash contentHash,
        string payload,
        short httpStatus,
        DateTimeOffset fetchedAt)
        : base(id)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Posting source id must not be empty.", nameof(sourceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentNullException.ThrowIfNull(contentHash);
        ArgumentNullException.ThrowIfNull(payload);

        SourceId = sourceId;
        ExternalId = externalId;
        ContentHash = contentHash;
        Payload = payload;
        HttpStatus = httpStatus;
        FetchedAt = fetchedAt;
        LastSeenAt = fetchedAt;
    }

    private RawPosting()
    {
        ExternalId = string.Empty;
        Payload = string.Empty;
        ContentHash = null!;
    }

    public Guid SourceId { get; private set; }

    /// <summary>The provider's own posting id.</summary>
    public string ExternalId { get; private set; }

    public ContentHash ContentHash { get; private set; }

    /// <summary>The verbatim provider payload — never edited (invariant 1).</summary>
    public string Payload { get; private set; }

    public short HttpStatus { get; private set; }

    /// <summary>First time this exact content was seen.</summary>
    public DateTimeOffset FetchedAt { get; private set; }

    /// <summary>Bumped on every unchanged re-fetch (AC-02).</summary>
    public DateTimeOffset LastSeenAt { get; private set; }
}
