using JobHunter.Domain.Common;

namespace JobHunter.Domain.Profiles;

/// <summary>
/// One immutable version of the Owner's CV (data-model §cv_versions, ADR-F4-0002). A new upload is a new
/// row, never an edit: a match made against an older CV remains the honest record of what was true then.
///
/// <para><see cref="ExtractedText"/> is the <strong>single</strong> storage location for CV content in
/// the whole schema — nothing else, no index, no cache, no log, may hold it (invariant: the CV crosses
/// exactly one boundary, the F4 match prompt). There is no setter for it; it is fixed at construction.
/// The original binary is discarded after extraction — less data at rest, and no file to serve.</para>
/// </summary>
public sealed class CvVersion : Entity
{
    /// <summary>A SHA-256 hex digest is exactly 64 characters; re-uploading identical content is a no-op.</summary>
    public const int ContentHashLength = 64;

    /// <summary>
    /// Builds a CV version. Every field is required: the version number is monotonic per profile, the
    /// content hash is a 64-character SHA-256 digest (so an identical re-upload is recognised, not
    /// re-versioned), and the extracted text is the sole CV-bearing value. A blank hash, name, media type
    /// or extracted text is a programmer error, not a business outcome.
    /// </summary>
    public CvVersion(
        Guid id,
        Guid profileId,
        short version,
        bool isActive,
        string fileName,
        string mediaType,
        int sizeBytes,
        string contentHash,
        string extractedText,
        DateTimeOffset uploadedAt,
        DateTimeOffset? activatedAt)
        : base(id)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A CV version must belong to a Profile.", nameof(profileId));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A CV version number is 1-based and positive.");
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "A CV must have a positive size.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedText);

        var hash = contentHash.Trim().ToLowerInvariant();
        if (hash.Length != ContentHashLength || !hash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "A CV content hash must be a 64-character SHA-256 hex digest.",
                nameof(contentHash));
        }

        ProfileId = profileId;
        Version = version;
        IsActive = isActive;
        FileName = fileName.Trim();
        MediaType = mediaType.Trim();
        SizeBytes = sizeBytes;
        ContentHash = hash;
        ExtractedText = extractedText;
        UploadedAt = uploadedAt;
        ActivatedAt = activatedAt;
    }

    private CvVersion()
    {
    }

    public Guid ProfileId { get; private init; }

    /// <summary>Monotonic per profile; the first upload is version 1.</summary>
    public short Version { get; private init; }

    /// <summary>True for the one active version per profile (partial unique index enforces exactly one).</summary>
    public bool IsActive { get; private init; }

    public string FileName { get; private init; } = null!;

    /// <summary>The media type <em>sniffed</em> from content, not taken from the file extension.</summary>
    public string MediaType { get; private init; } = null!;

    public int SizeBytes { get; private init; }

    /// <summary>The lower-cased SHA-256 hex digest of the uploaded content.</summary>
    public string ContentHash { get; private init; } = null!;

    /// <summary>
    /// The extracted CV text — the only CV-bearing column in the entire schema. It has no setter and is
    /// never copied onto a log, span, index or notification.
    /// </summary>
    public string ExtractedText { get; private init; } = null!;

    public DateTimeOffset UploadedAt { get; private init; }

    /// <summary>When this version became active, or null if it never has.</summary>
    public DateTimeOffset? ActivatedAt { get; private init; }
}
