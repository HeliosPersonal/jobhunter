namespace JobHunter.Domain.Profiles;

/// <summary>
/// The media types a CV may be uploaded as. The type is <em>sniffed from the content's magic bytes</em>,
/// never trusted from the file extension (security §5): a <c>.pdf</c> extension on a ZIP is
/// <see cref="Unknown"/> and refused. Only <see cref="Pdf"/> and <see cref="Markdown"/> (plain,
/// UTF-8-decodable text) are accepted; everything else is <see cref="Unknown"/>.
/// </summary>
public enum CvMediaType
{
    /// <summary>The content matched no accepted signature — refused, never guessed.</summary>
    Unknown = 0,

    /// <summary>A PDF (<c>%PDF</c> header). Text is extracted in-process; there is no OCR.</summary>
    Pdf = 1,

    /// <summary>Plain, UTF-8-decodable text — Markdown or plain text; used verbatim as the CV text.</summary>
    Markdown = 2,
}

/// <summary>
/// The pure content sniffer behind CV upload (security §5, T03). It reads a file's leading bytes and
/// classifies it by signature — never by the caller-supplied extension — so a hostile or mislabelled
/// upload (a ZIP renamed <c>cv.pdf</c>, a binary blob) is <see cref="CvMediaType.Unknown"/> and refused
/// before any extraction is attempted. It has no dependencies and does no I/O: it is the one place the
/// "sniffed, not trusted" rule is decided, and it is unit-tested directly.
/// </summary>
public static class CvMediaTypeSniffer
{
    private static readonly byte[] PdfSignature = "%PDF"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Classifies <paramref name="content"/> by its leading bytes. A PDF header wins first; a ZIP header
    /// (a renamed archive or an Office document) is explicitly refused as <see cref="CvMediaType.Unknown"/>;
    /// otherwise content that decodes as printable UTF-8 text is <see cref="CvMediaType.Markdown"/>, and
    /// anything binary is <see cref="CvMediaType.Unknown"/>. Empty content is <see cref="CvMediaType.Unknown"/>.
    /// </summary>
    public static CvMediaType Sniff(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return CvMediaType.Unknown;
        }

        if (content.StartsWith(PdfSignature))
        {
            return CvMediaType.Pdf;
        }

        // A ZIP container (and therefore a .docx, .xlsx or a renamed archive) is refused outright, even
        // though its first bytes could otherwise be mistaken for text once past the header.
        if (content.StartsWith(ZipSignature))
        {
            return CvMediaType.Unknown;
        }

        return LooksLikeText(content) ? CvMediaType.Markdown : CvMediaType.Unknown;
    }

    /// <summary>The stored <c>media_type</c> string for a sniffed type (data-model §cv_versions).</summary>
    public static string ToMediaTypeString(this CvMediaType mediaType) => mediaType switch
    {
        CvMediaType.Pdf => "application/pdf",
        CvMediaType.Markdown => "text/markdown",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "Unknown media type has no stored form."),
    };

    // Text if a leading window carries no NUL and no C0 control byte other than tab, newline and carriage
    // return. A single NUL is the classic binary tell; the control-byte guard rejects the rest of the
    // non-text bytes a real document never contains.
    private static bool LooksLikeText(ReadOnlySpan<byte> content)
    {
        var window = content.Length > 512 ? content[..512] : content;
        foreach (var b in window)
        {
            if (b == 0x00)
            {
                return false;
            }

            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D))
            {
                return false;
            }
        }

        return true;
    }
}
