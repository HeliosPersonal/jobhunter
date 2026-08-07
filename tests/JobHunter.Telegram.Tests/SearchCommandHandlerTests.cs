using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Search;
using JobHunter.TestKit;
using JobHunter.Telegram.Search;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests;

/// <summary>
/// The <c>/search</c> command end to end over a zero-network <see cref="ISearchQuery"/> substitute
/// (F9-T09): it runs the parsed query through the shared query port (the O12 decision — the same service
/// the API uses) and renders the digest card layout. An unreachable index (an <see cref="ISearchQuery"/>
/// failure, QG-3) produces a clear "unavailable" message and never throws, so a Typesense outage degrades
/// only <c>/search</c> and leaves other commands working (DoD).
/// </summary>
public sealed class SearchCommandHandlerTests
{
    private readonly ISearchQuery _search = Substitute.For<ISearchQuery>();

    private readonly FakeClock _clock = new(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

    private SearchCommandHandler NewHandler() =>
        new(_search, _clock, NullLogger<SearchCommandHandler>.Instance);

    [Fact]
    public async Task It_passes_the_parsed_typed_query_to_the_shared_search_port()
    {
        SearchQuery? captured = null;
        _search.SearchAsync(Arg.Do<SearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(EmptyResults()));

        await NewHandler().HandleAsync("staff sre tech:go remote:remote");

        captured.ShouldNotBeNull();
        captured.Text.ShouldBe("staff sre");
        captured.Technologies.ShouldBe(["go"]);
        captured.RemotePolicies.ShouldBe(["remote"]);
        captured.Limit.ShouldBe(10);
    }

    [Fact]
    public async Task It_renders_results_in_the_card_layout()
    {
        _search.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(OneResult()));

        var rendered = await NewHandler().HandleAsync("platform");

        rendered.ShouldContain("Platform Engineer");
        rendered.ShouldContain("Acme");
    }

    [Fact]
    public async Task An_unreachable_index_produces_a_clear_message_and_does_not_throw()
    {
        _search.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Failure(new Error("search.unavailable", "down")));

        var rendered = await NewHandler().HandleAsync("anything");

        rendered.ShouldContain("unavailable", Case.Insensitive);
    }

    [Fact]
    public async Task No_results_produces_a_helpful_broaden_message()
    {
        _search.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(EmptyResults()));

        var rendered = await NewHandler().HandleAsync("kubernetes on mars");

        rendered.ShouldContain("No results");
    }

    [Fact]
    public async Task A_null_argument_string_is_a_valid_filters_only_search()
    {
        _search.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(EmptyResults()));

        var rendered = await NewHandler().HandleAsync(null);

        rendered.ShouldContain("No results");
    }

    [Fact]
    public async Task It_resolves_a_relative_since_window_against_the_clock()
    {
        SearchQuery? captured = null;
        _search.SearchAsync(Arg.Do<SearchQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Result<SearchResults>.Success(EmptyResults()));

        await NewHandler().HandleAsync("since:7d kafka");

        captured.ShouldNotBeNull();
        captured.PostedAfter.ShouldBe(1_700_000_000L - (7L * 86_400L));
        captured.Text.ShouldBe("kafka");
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new SearchCommandHandler(null!, _clock, NullLogger<SearchCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new SearchCommandHandler(_search, null!, NullLogger<SearchCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new SearchCommandHandler(_search, _clock, null!));
    }

    private static SearchResults EmptyResults() =>
        new([], 0, new Dictionary<string, IReadOnlyList<FacetCount>>(), NextCursor: null, Partial: false);

    private static SearchResults OneResult()
    {
        var doc = new JobDocument(
            Id: Guid.NewGuid().ToString(),
            Title: "Platform Engineer",
            CompanyName: "Acme",
            CompanyDomain: "acme.com",
            Description: "A role.",
            Technologies: ["Go"],
            Countries: ["Germany"],
            RemotePolicy: "Remote",
            Seniority: "Senior",
            EmploymentType: "FullTime",
            CompanyStage: null,
            AiUsage: null,
            SalaryMin: null,
            SalaryMax: null,
            SalaryCurrency: null,
            Score: 0d,
            PostedAt: null,
            FirstSeenAt: 1_700_000_000,
            Status: "Live",
            ApplicationStatus: null);
        return new SearchResults(
            [new SearchHit(doc, Highlight: null)],
            1,
            new Dictionary<string, IReadOnlyList<FacetCount>>(),
            NextCursor: null,
            Partial: false);
    }
}
