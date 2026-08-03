using System.Security.Cryptography;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Profiles;

namespace JobHunter.Application.Profiles;

/// <summary>
/// The CV upload application service (T03, ADR-F4-0002). It is the whole write path for a CV: sniff the
/// media type from content (never the extension), refuse anything over the size cap <em>before</em>
/// extraction, extract text in-process, hash the bytes, no-op on identical content, and otherwise insert
/// a new immutable version — deactivating the previous active one atomically. The uploaded binary is
/// never persisted: only the extracted text and a hash of the bytes survive this method, which is what
/// keeps the personal-data surface to one column.
///
/// <para>Every branch that a caller could hit is a <see cref="Result{T}"/> failure with a coded, non-CV
/// message — an oversize file, an unrecognised type, a text-less PDF. The CV text itself never appears in
/// a return value, a log or an exception; it is handed to the repository and to nothing else.</para>
/// </summary>
public sealed class CvUploadService(
    IProfileRepository profiles,
    ICvVersionRepository cvVersions,
    ICvTextExtractor extractor,
    IIdGenerator ids,
    IClock clock)
{
    /// <summary>The hard cap enforced before extraction begins (security §5): 5 MB.</summary>
    public const int MaxSizeBytes = 5 * 1024 * 1024;

    /// <summary>The error codes the upload can fail with; the endpoint maps each to an HTTP status.</summary>
    public static class Errors
    {
        public const string NoActiveProfile = "cv.no_active_profile";
        public const string Empty = "cv.empty";
        public const string TooLarge = "cv.too_large";
        public const string UnsupportedType = "cv.unsupported_type";
        public const string NoText = "cv.no_extractable_text";
    }

    /// <summary>
    /// Uploads <paramref name="content"/> as the active Profile's CV. <paramref name="fileName"/> is kept
    /// for display only — it is never trusted for the media type. Returns the resulting version's metadata
    /// (with <c>Created = false</c> when identical content was already stored), or a coded failure.
    /// </summary>
    public async Task<Result<CvUploadResult>> UploadAsync(
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (content.IsEmpty)
        {
            return new Error(Errors.Empty, "The uploaded CV is empty.");
        }

        // The cap is checked before extraction, so a hostile 500 MB upload is refused without ever being
        // parsed (security §5): the size gate is a precondition, not a post-hoc check.
        if (content.Length > MaxSizeBytes)
        {
            return new Error(
                Errors.TooLarge,
                $"The uploaded CV is {content.Length} bytes; the maximum is {MaxSizeBytes} bytes.");
        }

        var profile = await profiles.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return new Error(Errors.NoActiveProfile, "There is no active Profile to attach a CV to.");
        }

        var mediaType = CvMediaTypeSniffer.Sniff(content.Span);
        if (mediaType == CvMediaType.Unknown)
        {
            return new Error(
                Errors.UnsupportedType,
                "The uploaded file is not a supported CV type. Only PDF, Markdown and plain text are accepted.");
        }

        var contentHash = Sha256Hex(content.Span);

        // Content-hash no-op: identical bytes for this profile are the same CV, not a new version. Return
        // the existing version unchanged rather than inserting a duplicate that uq_cv_versions_hash would
        // reject anyway (ADR-F4-0002).
        var existing = await cvVersions.FindByHashAsync(profile.Id, contentHash, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new CvUploadResult(existing.Id, existing.Version, Created: false);
        }

        var extracted = extractor.Extract(mediaType, content);
        if (extracted.IsFailure)
        {
            // A PDF with no embedded text is a scan; the system does not OCR (security §5). The failure the
            // extractor returns already carries a clear, CV-free message — propagate it, do not wrap it.
            return extracted.Error;
        }

        var version = await cvVersions.NextVersionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var cvVersion = new CvVersion(
            ids.NewId(),
            profile.Id,
            version,
            isActive: true,
            fileName.Trim(),
            mediaType.ToMediaTypeString(),
            content.Length,
            contentHash,
            extracted.Value,
            uploadedAt: now,
            activatedAt: now);

        // Deactivate the previous active version and insert the new one in one transaction: the partial
        // unique index never sees two active rows for the profile.
        await cvVersions.ActivateAsync(cvVersion, cancellationToken).ConfigureAwait(false);

        // The binary goes out of scope here and is never written anywhere: only the extracted text (on the
        // version row) and the content hash survive.
        return new CvUploadResult(cvVersion.Id, cvVersion.Version, Created: true);
    }

    private static string Sha256Hex(ReadOnlySpan<byte> content)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(content, digest);
        return Convert.ToHexStringLower(digest);
    }
}
