using System.Text;
using JobHunter.Domain.Profiles;
using JobHunter.Infrastructure.Cv;
using Shouldly;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Cv;

/// <summary>
/// The in-process CV text extractor (T03, security §5). It runs entirely in-process — the fixtures are
/// real PDFs built with PdfPig in the same process, no shell-out, no external tool — and it extracts only
/// embedded text: a PDF with no text object yields nothing and is refused rather than OCR'd. Markdown and
/// plain text are decoded as UTF-8; invalid UTF-8 that slipped past the sniffer is treated as text-less.
/// </summary>
public sealed class CvTextExtractorTests
{
    private readonly CvTextExtractor _extractor = new();

    [Fact]
    public void A_pdf_with_embedded_text_is_extracted()
    {
        var pdf = BuildPdf("Senior Platform Engineer — Go, Kubernetes, PostgreSQL.");

        var result = _extractor.Extract(CvMediaType.Pdf, pdf);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain("Senior Platform Engineer");
        result.Value.ShouldContain("Kubernetes");
    }

    [Fact]
    public void A_pdf_with_no_embedded_text_is_refused_rather_than_ocr()
    {
        // A page with no text object at all — the shape a scanned, image-only CV extracts to.
        var pdf = BuildEmptyPdf();

        var result = _extractor.Extract(CvMediaType.Pdf, pdf);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CvTextExtractor.NoTextCode);
        result.Error.Message.ShouldContain("does not OCR");
    }

    [Fact]
    public void Markdown_is_decoded_as_utf8_text()
    {
        var content = Encoding.UTF8.GetBytes("# CV\n\nStaff engineer — café-grade résumé.\n");

        var result = _extractor.Extract(CvMediaType.Markdown, content);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain("Staff engineer");
        result.Value.ShouldContain("résumé");
    }

    [Fact]
    public void Whitespace_only_text_is_refused()
    {
        var result = _extractor.Extract(CvMediaType.Markdown, Encoding.UTF8.GetBytes("   \n\t  \n"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CvTextExtractor.NoTextCode);
    }

    [Fact]
    public void Invalid_utf8_is_treated_as_text_less()
    {
        // A lone continuation byte 0x80 is not valid UTF-8.
        byte[] invalid = [0x41, 0x80, 0x42];

        var result = _extractor.Extract(CvMediaType.Markdown, invalid);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CvTextExtractor.NoTextCode);
    }

    [Fact]
    public void An_unknown_media_type_is_unsupported()
    {
        var result = _extractor.Extract(CvMediaType.Unknown, new byte[] { 1, 2, 3 });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CvTextExtractor.UnsupportedCode);
    }

    private static byte[] BuildPdf(string text)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(50, 700), font);
        return builder.Build();
    }

    private static byte[] BuildEmptyPdf()
    {
        using var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        return builder.Build();
    }
}
