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
/// The three remaining Tier-1 adapters (Lever, Ashby, Workable) against the recorded corpus, zero network.
/// The streaming, malformed-survival and empty-board behaviour is shared with Greenhouse via the base
/// class; these facts pin each provider's distinct external-id shape, array location, URL and expected
/// posting count so a contract drift is caught as a fixture failure, not in production.
/// </summary>
public sealed class TierOneAdaptersTests
{
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    public static TheoryData<string> Providers => new() { "lever", "ashby", "workable" };

    private static JsonBoardJobSource Build(string provider, string body, out StubHttpMessageHandler handler)
    {
        handler = StubHttpMessageHandler.WithBody(body);
        var gated = new GatedHttpClient(new StubHttpClientFactory(handler));
        return provider switch
        {
            "lever" => new LeverJobSource(gated, NullLogger<LeverJobSource>.Instance),
            "ashby" => new AshbyJobSource(gated, NullLogger<AshbyJobSource>.Instance),
            "workable" => new WorkableJobSource(gated, NullLogger<WorkableJobSource>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }

    private static AtsBinding BindingFor(string provider)
    {
        var kind = provider switch
        {
            "lever" => AtsKind.Lever,
            "ashby" => AtsKind.Ashby,
            "workable" => AtsKind.Workable,
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

        return new AtsBinding(
            Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
            CompanyId,
            kind,
            "acme",
            BindingConfidence.TryCreate(0.95m).Value,
            "{}",
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static async Task<List<FetchedPosting>> DrainAsync(string provider, string fixture)
    {
        var source = Build(provider, Fixtures.Load(provider, fixture), out _);
        var result = new List<FetchedPosting>();
        await foreach (var posting in source.FetchAsync(BindingFor(provider), CancellationToken.None))
        {
            result.Add(posting);
        }

        return result;
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task HappyBoard_streamsTwentyPostings_withUniqueIds(string provider)
    {
        var postings = await DrainAsync(provider, "happy-20-postings.json");

        postings.Count.ShouldBe(20);
        postings.Select(p => p.ExternalId).ShouldBeUnique();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task EmptyBoard_isSuccessNotError(string provider)
    {
        (await DrainAsync(provider, "empty-board.json")).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task SinglePosting_yieldsOne_withAContentHash(string provider)
    {
        var postings = await DrainAsync(provider, "single-posting.json");

        postings.Count.ShouldBe(1);
        postings[0].ContentHash.Length.ShouldBe(64);
        postings[0].ExternalId.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task TruncatedTail_keepsTheIntactLeadingPosting(string provider)
    {
        var postings = await DrainAsync(provider, "malformed-truncated.json");

        postings.Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task MissingOptionalFields_doesNotThrow(string provider)
    {
        (await DrainAsync(provider, "missing-optional-fields.json")).Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task UnicodeAndHtml_surviveRoundTrip(string provider)
    {
        var postings = await DrainAsync(provider, "unicode-and-html.json");

        postings.Count.ShouldBe(1);
        postings[0].RawPayload.ShouldContain("日本");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task PostingsWithNoId_areSkipped(string provider)
    {
        (await DrainAsync(provider, "ids-absent.json")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Lever_buildsRootArrayUrl_andReadsUuidId()
    {
        var source = Build("lever", Fixtures.Load("lever", "single-posting.json"), out var handler);
        var postings = new List<FetchedPosting>();
        await foreach (var p in source.FetchAsync(BindingFor("lever"), CancellationToken.None))
        {
            postings.Add(p);
        }

        postings[0].ExternalId.ShouldBe("a1b2c3d4-0001-4aaa-8bbb-000000000001");
        handler.LastRequestUri!.ToString().ShouldBe("https://api.lever.co/v0/postings/acme?mode=json");
    }

    [Fact]
    public async Task Ashby_buildsCompensationUrl_andStripsUpdatedAtFromHash()
    {
        var original = await DrainAsync("ashby", "single-posting.json");
        var touched = await DrainAsync("ashby", "single-posting-cosmetic-touch.json");

        touched[0].ContentHash.ShouldBe(original[0].ContentHash);
        touched[0].RawPayload.ShouldNotBe(original[0].RawPayload);

        var source = Build("ashby", Fixtures.Load("ashby", "single-posting.json"), out var handler);
        await foreach (var _ in source.FetchAsync(BindingFor("ashby"), CancellationToken.None))
        {
        }

        handler.LastRequestUri!.ToString()
            .ShouldBe("https://api.ashbyhq.com/posting-api/job-board/acme?includeCompensation=true");
    }

    [Fact]
    public async Task Workable_buildsDetailsUrl_andReadsShortcode()
    {
        var source = Build("workable", Fixtures.Load("workable", "single-posting.json"), out var handler);
        var postings = new List<FetchedPosting>();
        await foreach (var p in source.FetchAsync(BindingFor("workable"), CancellationToken.None))
        {
            postings.Add(p);
        }

        postings[0].ExternalId.ShouldBe("ABCDE12345");
        handler.LastRequestUri!.ToString()
            .ShouldBe("https://apply.workable.com/api/v1/widget/accounts/acme?details=true");
    }

    [Fact]
    public void Kinds_areDistinctPerAdapter()
    {
        Build("lever", "[]", out _).Kind.ShouldBe(AtsKind.Lever);
        Build("ashby", "{\"jobs\":[]}", out _).Kind.ShouldBe(AtsKind.Ashby);
        Build("workable", "{\"jobs\":[]}", out _).Kind.ShouldBe(AtsKind.Workable);
    }
}
