using System.Text;
using JobHunter.Infrastructure.Http;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

public sealed class CappedContentTests
{
    [Fact]
    public async Task A_body_within_the_cap_reads_whole()
    {
        using var inner = new ByteArrayContent(Encoding.UTF8.GetBytes("hello world"));
        using var capped = new CappedContent(inner, maxBytes: 1024);

        await using var stream = await capped.ReadCappedAsync();
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();

        text.ShouldBe("hello world");
    }

    [Fact]
    public async Task A_body_over_the_cap_is_abandoned_mid_stream()
    {
        var payload = new byte[2048];
        using var inner = new ByteArrayContent(payload);
        using var capped = new CappedContent(inner, maxBytes: 1024);

        await Should.ThrowAsync<ResponseTooLargeException>(() => capped.ReadCappedAsync());
    }

    [Fact]
    public async Task A_body_exactly_at_the_cap_is_allowed()
    {
        var payload = new byte[1024];
        using var inner = new ByteArrayContent(payload);
        using var capped = new CappedContent(inner, maxBytes: 1024);

        await using var stream = await capped.ReadCappedAsync();

        stream.Length.ShouldBe(1024);
    }

    [Fact]
    public void A_non_positive_cap_is_rejected()
    {
        using var inner = new ByteArrayContent([1, 2, 3]);

        Should.Throw<ArgumentOutOfRangeException>(() => new CappedContent(inner, maxBytes: 0));
    }

    [Fact]
    public async Task Serializing_to_a_stream_enforces_the_cap()
    {
        var payload = new byte[4096];
        using var inner = new ByteArrayContent(payload);
        using var capped = new CappedContent(inner, maxBytes: 1024);
        using var destination = new MemoryStream();

        await Should.ThrowAsync<ResponseTooLargeException>(() => capped.CopyToAsync(destination));
    }

    [Fact]
    public void The_exception_reports_the_cap()
    {
        var ex = new ResponseTooLargeException(1024);

        ex.MaxBytes.ShouldBe(1024);
    }
}
