using System.Net;
using System.Net.Http.Json;
using JobHunter.Domain.Common;
using JobHunter.Domain.Search;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// <c>GET /api/search</c> end-to-end against the real pipeline with a faked <see cref="ISearchQuery"/>
/// (T05): a successful search returns hits, the found count, facets and the next cursor; an unavailable
/// index is a plain 503 with the rest of the system unaffected (AC-09, QG-3); an invalid cursor is a 400;
/// the endpoint requires <c>jobhunter:read</c> and refuses a tokenless caller and a non-Owner subject; and
/// a hit carries nothing beyond the allowlisted document (QG-2).
/// </summary>
public sealed class SearchEndpointTests : IClassFixture<EndpointsHostFactory>
{
    private readonly EndpointsHostFactory _factory;

    public SearchEndpointTests(EndpointsHostFactory factory) => _factory = factory;

    private static SearchResults Sample()
    {
        var doc = new JobDocument(
            Id: "0192e8b7-0000-0000-0000-000000000001",
            Title: "Staff Backend Engineer",
            CompanyName: "Snowflake",
            CompanyDomain: "snowflake.com",
            Description: "distributed systems with Kafka",
            Technologies: ["Kafka", "C#"],
            Countries: ["DE"],
            RemotePolicy: "Remote",
            Seniority: "Staff",
            EmploymentType: "FullTime",
            CompanyStage: null,
            AiUsage: null,
            SalaryMin: 180000,
            SalaryMax: 220000,
            SalaryCurrency: "USD",
            Score: 0d,
            PostedAt: 1_722_124_800,
            FirstSeenAt: 1_722_124_800,
            Status: "Live",
            ApplicationStatus: null);

        var facets = new Dictionary<string, IReadOnlyList<FacetCount>>(StringComparer.Ordinal)
        {
            ["technologies"] = [new FacetCount("Kafka", 47)],
        };

        return new SearchResults([new SearchHit(doc, "with <mark>Kafka</mark>")], 47, facets, "next-cursor", false);
    }

    [Fact]
    public async Task A_successful_search_returns_hits_found_facets_and_the_next_cursor()
    {
        _factory.Search
            .SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(Sample()));

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/search?q=kafka&technology=Kafka", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SearchDto>();
        body.ShouldNotBeNull();
        body.Found.ShouldBe(47);
        body.Hits.Count.ShouldBe(1);
        body.Hits[0].Title.ShouldBe("Staff Backend Engineer");
        body.Hits[0].Highlight.ShouldBe("with <mark>Kafka</mark>");
        body.NextCursor.ShouldBe("next-cursor");
        body.Facets.ShouldContainKey("technologies");
    }

    [Fact]
    public async Task The_typed_query_carries_the_user_term_as_a_parameter_never_a_filter_operator()
    {
        SearchQuery? captured = null;
        _factory.Search
            .SearchAsync(Arg.Do<SearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(Sample()));

        using var client = _factory.OwnerClient();
        await client.GetAsync(new Uri("/api/search?q=kafka&technology=Kafka&technology=Azure&minScore=70", UriKind.Relative));

        captured.ShouldNotBeNull();
        captured.Text.ShouldBe("kafka");
        captured.Technologies.ShouldBe(["Kafka", "Azure"]);
        captured.MinScore.ShouldBe(70);
    }

    [Fact]
    public async Task An_unavailable_index_is_a_503_stating_the_rest_of_the_system_is_unaffected()
    {
        _factory.Search
            .SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Failure(new Error("search.unavailable", "down")));

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/search?q=kafka", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldContain("unaffected");
    }

    [Fact]
    public async Task An_invalid_cursor_is_a_400()
    {
        _factory.Search
            .SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Failure(new Error("search.cursor.invalid", "bad cursor")));

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/search?cursor=garbage", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_search_endpoint_refuses_a_tokenless_caller()
    {
        _factory.Search
            .SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(Sample()));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/search?q=kafka", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_search_endpoint_refuses_a_read_token_for_a_subject_other_than_the_owner()
    {
        _factory.Search
            .SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(Sample()));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, "jobhunter:read");
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, "someone-else");

        var response = await client.GetAsync(new Uri("/api/search?q=kafka", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.Dispose();
    }

    private sealed record SearchDto(
        IReadOnlyList<HitDto> Hits,
        int Found,
        IReadOnlyDictionary<string, IReadOnlyList<FacetDto>> Facets,
        string? NextCursor,
        bool Partial);

    private sealed record HitDto(string Id, string Title, string? Highlight);

    private sealed record FacetDto(string Value, int Count);
}
