using System.Text;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Profiles;
using UglyToad.PdfPig;

namespace JobHunter.Infrastructure.Cv;

/// <summary>
/// The in-process CV text extractor (SAD §5 <c>PdfTextExtractor · MarkdownTextExtractor</c>, security §5).
/// PDF text comes from PdfPig, a pure-managed library that runs in the same process — no shell-out, no
/// external converter — and reads only the <em>embedded</em> text: a scanned, image-only PDF yields
/// nothing and is refused, because the system does not OCR. Markdown and plain text are decoded as
/// UTF-8. Both paths return a <see cref="Result{T}"/>: a text-less file is an expected business outcome,
/// not an exception. The extracted text is handed straight back and never logged (the CV-leakage rule).
/// </summary>
internal sealed class CvTextExtractor : ICvTextExtractor
{
    /// <summary>The failure code for a file that carried no extractable text (a scan, or empty text).</summary>
    public const string NoTextCode = "cv.no_extractable_text";

    /// <summary>The failure code for a media type this extractor was not built to handle.</summary>
    public const string UnsupportedCode = "cv.unsupported_type";

    public Result<string> Extract(CvMediaType mediaType, ReadOnlyMemory<byte> content) => mediaType switch
    {
        CvMediaType.Pdf => ExtractPdf(content),
        CvMediaType.Markdown => ExtractText(content),
        _ => new Error(UnsupportedCode, "The media type is not a supported CV type."),
    };

    private static Result<string> ExtractPdf(ReadOnlyMemory<byte> content)
    {
        using var document = PdfDocument.Open(content.ToArray());
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                builder.AppendLine(page.Text);
            }
        }

        var extracted = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(extracted)
            ? new Error(
                NoTextCode,
                "The PDF has no extractable text. A scanned or image-only CV is not supported — the system does not OCR.")
            : extracted;
    }

    private static Result<string> ExtractText(ReadOnlyMemory<byte> content)
    {
        // Strict UTF-8: content that was sniffed as text but is not valid UTF-8 is treated as text-less
        // rather than silently mangled with replacement characters.
        var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        string decoded;
        try
        {
            decoded = decoder.GetString(content.Span);
        }
        catch (DecoderFallbackException)
        {
            return new Error(NoTextCode, "The uploaded text could not be decoded as UTF-8.");
        }

        var trimmed = decoded.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? new Error(NoTextCode, "The uploaded file contains no text.")
            : trimmed;
    }
}
