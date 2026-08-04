using JobHunter.Domain.Common;
using JobHunter.Domain.Profiles;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The port for turning an uploaded CV's bytes into its text, in-process and with no shell-out
/// (security §5, SAD §5 <c>PdfTextExtractor · MarkdownTextExtractor</c>). Extraction is a business
/// operation with an expected failure — a PDF with no embedded text (a scan; there is no OCR) or a file
/// that decodes to nothing — so the outcome is a <see cref="Result{T}"/>, never an exception. The
/// concrete extractor lives in Infrastructure; the Application upload service depends only on this port.
/// </summary>
public interface ICvTextExtractor
{
    /// <summary>
    /// Extracts the CV text from <paramref name="content"/> for the already-sniffed
    /// <paramref name="mediaType"/>. Returns the extracted text on success, or a failure when the file
    /// carries no extractable text (refused with a clear message rather than OCR'd) or is unsupported.
    /// </summary>
    Result<string> Extract(CvMediaType mediaType, ReadOnlyMemory<byte> content);
}
