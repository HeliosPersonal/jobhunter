using System.Net;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.Scrapers.Adapters;
using JobHunter.Scrapers.Http;
using JobHunter.Scrapers.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Adapters;

/// <summary>
/// The careers adapter's discovery-facing path and identifier-shape arms that the fixture-driven
/// <see cref="CareersPageJobSourceTests"/> does not reach: <see cref="CareersPageJobSource.FetchBoardAsync"/>,
/// which reports the terminal <see cref="FetchOutcome"/> alongside the streamed postings (success and HTTP
/// error), and the <c>PropertyValue.value</c> identifier arms — a value that is a plain number (read verbatim)
/// versus a value that is neither string nor number (the switch's default, which falls back to hashing the apply
/// URL). All zero-network, driven by an inline HTML body.
/// </summary>
public sealed class CareersPageJobSourceBranchTests
{
    private const string CareersUrl = "https://acme.example/careers";

    private static CareersPageJobSource Source(string html, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = StubHttpMessageHandler.WithBody(html, status);
        return new CareersPageJobSource(
            new GatedHttpClient(new StubHttpClientFactory(handler)),
            NullLogger<CareersPageJobSource>.Instance);
    }

    private static AtsBinding Binding() => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000c1"),
        Guid.Parse("00000000-0000-0000-0000-0000000000d1"),
        AtsKind.CareersPage,
        CareersUrl,
        BindingConfidence.TryCreate(0.70m).Value,
        "{}",
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private static string PageWith(string identifierJson) =>
        "<script type=\"application/ld+json\">" +
        "{\"@type\":\"JobPosting\",\"identifier\":" + identifierJson +
        ",\"url\":\"https://acme.example/careers/role\"}" +
        "</script>";

    private static async Task<List<FetchedPosting>> Drain(SourceFetch fetch)
    {
        var result = new List<FetchedPosting>();
        await foreach (var p in fetch.Postings)
        {
            result.Add(p);
        }

        return result;
    }

    [Fact]
    public async Task FetchBoard_reports_success_and_streams_the_postings()
    {
        var fetch = await Source(PageWith("\"acme-eng-77\"")).FetchBoardAsync(Binding(), CancellationToken.None);

        fetch.Outcome.ShouldBe(FetchOutcome.Success);
        fetch.IsSuccess.ShouldBeTrue();
        fetch.HttpStatus.ShouldBe((short)200);
        var postings = await Drain(fetch);
        postings.ShouldHaveSingleItem().ExternalId.ShouldBe("acme-eng-77");
    }

    [Fact]
    public async Task FetchBoard_reports_an_http_error_and_an_empty_stream()
    {
        var fetch = await Source("nope", HttpStatusCode.InternalServerError)
            .FetchBoardAsync(Binding(), CancellationToken.None);

        // The terminal outcome — which an empty FetchAsync stream cannot convey — is classified as an HTTP error.
        fetch.Outcome.ShouldBe(FetchOutcome.HttpError);
        fetch.IsSuccess.ShouldBeFalse();
        (await Drain(fetch)).ShouldBeEmpty();
    }

    [Fact]
    public async Task FetchBoard_rejects_a_null_binding()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => Source("<html></html>").FetchBoardAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task A_property_value_identifier_carrying_a_number_is_read_verbatim()
    {
        // ReadIdentifier: Object-with-value whose value is a Number — the GetRawText arm of the inner switch.
        var fetch = await Source(PageWith("{\"@type\":\"PropertyValue\",\"value\":90210}"))
            .FetchBoardAsync(Binding(), CancellationToken.None);

        (await Drain(fetch)).ShouldHaveSingleItem().ExternalId.ShouldBe("90210");
    }

    [Fact]
    public async Task A_property_value_identifier_whose_value_is_neither_string_nor_number_falls_back_to_the_url()
    {
        // ReadIdentifier: Object-with-value whose value is a bool — the inner switch's default arm returns null,
        // so ReadExternalId synthesises the id from the apply URL instead.
        var fetch = await Source(PageWith("{\"@type\":\"PropertyValue\",\"value\":true}"))
            .FetchBoardAsync(Binding(), CancellationToken.None);

        (await Drain(fetch)).ShouldHaveSingleItem().ExternalId.ShouldStartWith("url:");
    }
}
