using System.Net;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Scrapers.Adapters;
using JobHunter.Scrapers.Http;
using JobHunter.Scrapers.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Adapters;

/// <summary>
/// The Tier-2 JSON-LD careers adapter (T07) against recorded pages, zero network. The suite pins each
/// "Done when" clause: multiple blocks with non-JobPosting types ignored, @graph and array wrapping,
/// survival of a malformed block, a synthesised id from the apply URL when identifier is absent, and the
/// cosmetic-touch stability that dateModified is stripped before hashing.
/// </summary>
public sealed class CareersPageJobSourceTests
{
    private const string CareersUrl = "https://acme.example/careers";

    private static CareersPageJobSource Source(string html, out StubHttpMessageHandler handler)
    {
        handler = StubHttpMessageHandler.WithBody(html);
        var gated = new GatedHttpClient(new StubHttpClientFactory(handler));
        return new CareersPageJobSource(gated, NullLogger<CareersPageJobSource>.Instance);
    }

    private static AtsBinding Binding(string careersUrl = CareersUrl) => new(
        Guid.Parse("00000000-0000-0000-0000-0000000000c1"),
        Guid.Parse("00000000-0000-0000-0000-0000000000d1"),
        AtsKind.CareersPage,
        careersUrl,
        BindingConfidence.TryCreate(0.70m).Value,
        "{}",
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private static async Task<List<FetchedPosting>> Drain(string fixture, string careersUrl = CareersUrl)
    {
        var source = Source(Fixtures.Load("careers", fixture), out _);
        var result = new List<FetchedPosting>();
        await foreach (var p in source.FetchAsync(Binding(careersUrl), CancellationToken.None))
        {
            result.Add(p);
        }

        return result;
    }

    [Fact]
    public void Kind_isCareersPage()
    {
        Source("<html></html>", out _).Kind.ShouldBe(AtsKind.CareersPage);
    }

    [Fact]
    public async Task NullBinding_throws()
    {
        var source = Source("<html></html>", out _);

        await Should.ThrowAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in source.FetchAsync(null!, CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task SinglePosting_yieldsOne_withIdentifierAndHash()
    {
        var postings = await Drain("single-jobposting.html");

        postings.Count.ShouldBe(1);
        postings[0].ExternalId.ShouldBe("acme-eng-001");
        postings[0].ContentHash.Length.ShouldBe(64);
        postings[0].RawPayload.ShouldContain("Senior Platform Engineer");
    }

    [Fact]
    public async Task Fetch_requestsTheCareersUrlVerbatim()
    {
        var source = Source(Fixtures.Load("careers", "single-jobposting.html"), out var handler);
        await foreach (var _ in source.FetchAsync(Binding(), CancellationToken.None))
        {
        }

        handler.LastRequestUri!.ToString().ShouldBe(CareersUrl);
    }

    [Fact]
    public async Task DateModified_isStrippedBeforeHashing()
    {
        var original = await Drain("single-jobposting.html");
        var touched = await Drain("single-jobposting-cosmetic-touch.html");

        touched[0].ContentHash.ShouldBe(original[0].ContentHash);
        touched[0].RawPayload.ShouldNotBe(original[0].RawPayload);
    }

    [Fact]
    public async Task MultipleBlocks_onlyJobPostingsKept_nonJobPostingIgnored()
    {
        var postings = await Drain("multiple-blocks-mixed-types.html");

        string[] expected = ["acme-eng-010", "acme-eng-011"];
        postings.Select(p => p.ExternalId).ShouldBe(expected);
    }

    [Fact]
    public async Task GraphWrapped_flattensEveryJobPosting()
    {
        var postings = await Drain("graph-wrapped.html", "https://globex.example/careers");

        string[] expected = ["globex-100", "globex-101"];
        postings.Select(p => p.ExternalId).ShouldBe(expected);
    }

    [Fact]
    public async Task ArrayWrapped_findsTheJobPostingAmongOtherTypes()
    {
        var postings = await Drain("array-wrapped.html", "https://initech.example/careers");

        postings.Count.ShouldBe(1);
        postings[0].ExternalId.ShouldBe("initech-7");
    }

    [Fact]
    public async Task MalformedBlock_doesNotSuppressTheValidOnes()
    {
        var postings = await Drain("malformed-block-among-valid.html", "https://umbrella.example/careers");

        string[] expected = ["umbrella-1", "umbrella-2"];
        postings.Select(p => p.ExternalId).ShouldBe(expected);
    }

    [Fact]
    public async Task MissingIdentifier_synthesisesIdByHashingApplyUrl()
    {
        var postings = await Drain("missing-identifier.html", "https://hooli.example/careers");

        postings.Count.ShouldBe(1);
        postings[0].ExternalId.ShouldStartWith("url:");
        postings[0].ExternalId.Length.ShouldBe("url:".Length + 64);
    }

    [Fact]
    public async Task NoIdentifierAndNoUrl_isSkipped()
    {
        (await Drain("no-identifier-no-url.html", "https://vandelay.example/careers")).ShouldBeEmpty();
    }

    [Fact]
    public async Task PageWithoutJsonLd_yieldsNothing()
    {
        (await Drain("no-jsonld.html", "https://wonka.example/careers")).ShouldBeEmpty();
    }

    [Fact]
    public async Task IdentifierShapes_propertyValueAndNumberAndTypeArray_areAllRead()
    {
        var postings = await Drain("identifier-shapes.html", "https://stark.example/careers");

        string[] expected = ["STARK-4210", "88117"];
        postings.Select(p => p.ExternalId).ShouldBe(expected);
        postings[0].RawPayload.ShouldContain("日本");
    }

    [Fact]
    public async Task NonOkResponse_yieldsNothing()
    {
        var handler = StubHttpMessageHandler.WithBody("nope", HttpStatusCode.InternalServerError);
        var source = new CareersPageJobSource(
            new GatedHttpClient(new StubHttpClientFactory(handler)),
            NullLogger<CareersPageJobSource>.Instance);

        var postings = new List<FetchedPosting>();
        await foreach (var p in source.FetchAsync(Binding(), CancellationToken.None))
        {
            postings.Add(p);
        }

        postings.ShouldBeEmpty();
    }
}
