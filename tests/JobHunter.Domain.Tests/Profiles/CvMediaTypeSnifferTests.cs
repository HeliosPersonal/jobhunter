using System.Text;
using JobHunter.Domain.Profiles;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Profiles;

/// <summary>
/// The content sniffer (T03, security §5): the media type is decided from the leading bytes, never the
/// caller's extension. A PDF header is a PDF, a ZIP header (a renamed archive, an Office document) is
/// refused as unknown even when it wears a <c>.pdf</c> name elsewhere, printable text is Markdown, and a
/// binary blob or empty content is unknown. This is the "sniffed, not trusted" rule as a unit test.
/// </summary>
public sealed class CvMediaTypeSnifferTests
{
    [Fact]
    public void A_pdf_header_is_sniffed_as_pdf()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ\n1 0 obj");

        CvMediaTypeSniffer.Sniff(pdf).ShouldBe(CvMediaType.Pdf);
    }

    [Fact]
    public void A_zip_header_is_refused_even_though_a_caller_might_name_it_pdf()
    {
        // PK\x03\x04 — a ZIP, and therefore a .docx or a renamed archive. Refused as unknown.
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];

        CvMediaTypeSniffer.Sniff(zip).ShouldBe(CvMediaType.Unknown);
    }

    [Fact]
    public void Printable_utf8_text_is_sniffed_as_markdown()
    {
        var text = Encoding.UTF8.GetBytes("# Senior Platform Engineer\n\nGo, Kubernetes, Postgres.\n");

        CvMediaTypeSniffer.Sniff(text).ShouldBe(CvMediaType.Markdown);
    }

    [Fact]
    public void Binary_content_with_a_nul_byte_is_unknown()
    {
        byte[] binary = [0x89, 0x50, 0x4E, 0x47, 0x00, 0x1A, 0x0A];

        CvMediaTypeSniffer.Sniff(binary).ShouldBe(CvMediaType.Unknown);
    }

    [Fact]
    public void Empty_content_is_unknown()
    {
        CvMediaTypeSniffer.Sniff([]).ShouldBe(CvMediaType.Unknown);
    }

    [Fact]
    public void Text_with_a_stray_control_byte_is_unknown()
    {
        // A bell character (0x07) is not tab/newline/carriage-return — the content is not plain text.
        byte[] withControl = Encoding.ASCII.GetBytes("hello\x07world");

        CvMediaTypeSniffer.Sniff(withControl).ShouldBe(CvMediaType.Unknown);
    }

    [Theory]
    [InlineData(CvMediaType.Pdf, "application/pdf")]
    [InlineData(CvMediaType.Markdown, "text/markdown")]
    public void The_stored_media_type_string_is_the_sniffed_type(CvMediaType mediaType, string expected)
    {
        mediaType.ToMediaTypeString().ShouldBe(expected);
    }

    [Fact]
    public void An_unknown_type_has_no_stored_form()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CvMediaType.Unknown.ToMediaTypeString());
    }
}
