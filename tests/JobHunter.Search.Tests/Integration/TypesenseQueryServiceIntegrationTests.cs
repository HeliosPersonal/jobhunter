using JobHunter.Domain.Search;
using JobHunter.Search;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Typesense;
using Xunit;

namespace JobHunter.Search.Tests.Integration;

/// <summary>
/// The real query round-trip against a Typesense container (F9-T03). This is the suite that proves the
/// behaviours a stub cannot: relevance-ordered hits with recognisable fields (AC-01), filters that narrow
/// with facet counts returned alongside (AC-02), typo tolerance returning the intended match for a
/// misspelled technology (AC-03), and closed jobs excluded by default and included only on the flag
/// (AC-08). Skips cleanly where Docker is absent.
/// </summary>
public sealed class TypesenseQueryServiceIntegrationTests : IAsyncLifetime
{
    private const string ApiKey = "test-api-key";

    private readonly TypesenseContainer _container = new TypesenseBuilder("typesense/typesense:27.1")
        .WithApiKey(ApiKey)
        .Build();

    private TypesenseIndexer _indexer = null!;
    private TypesenseQueryService _service = null!;

    public async Task InitializeAsync()
    {
        if (!DockerEnvironment.IsAvailable)
        {
            return;
        }

        await _container.StartAsync();

        var options = new TypesenseOptions
        {
            BaseUrl = new UriBuilder(Uri.UriSchemeHttp, _container.Hostname, _container.GetMappedPublicPort(8108)).Uri.ToString(),
            ApiKey = ApiKey,
            EnvironmentPrefix = "test",
        };

        var baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _indexer = new TypesenseIndexer(
            new HttpClient { BaseAddress = baseUri }, Options.Create(options), NullLogger<TypesenseIndexer>.Instance);
        _service = new TypesenseQueryService(
            new HttpClient { BaseAddress = baseUri }, Options.Create(options), NullLogger<TypesenseQueryService>.Instance);

        (await _indexer.EnsureCollectionAsync()).IsSuccess.ShouldBeTrue();
        (await _indexer.UpsertManyAsync(Corpus())).IsSuccess.ShouldBeTrue();
    }

    public async Task DisposeAsync()
    {
        if (DockerEnvironment.IsAvailable)
        {
            await _container.DisposeAsync();
        }
    }

    private static IReadOnlyList<JobDocument> Corpus() =>
    [
        Doc("00000000-0000-7000-8000-000000000001", "Staff Backend Engineer", "Snowflake", "snowflake.com",
            ["Kafka", "Azure", "C#"], score: 95, status: "Live"),
        Doc("00000000-0000-7000-8000-000000000002", "Senior Platform Engineer", "Acme", "acme.com",
            ["Kafka", "Kubernetes"], score: 80, status: "Live"),
        Doc("00000000-0000-7000-8000-000000000003", "Frontend Engineer", "Widgets", "widgets.com",
            ["TypeScript", "React"], score: 60, status: "Live"),
        Doc("00000000-0000-7000-8000-000000000004", "Retired Kafka Role", "Ghost", "ghost.com",
            ["Kafka"], score: 40, status: "Closed"),
    ];

    private static JobDocument Doc(
        string id, string title, string company, string domain, string[] tech, double score, string status) => new(
        Id: id, Title: title, CompanyName: company, CompanyDomain: domain,
        Description: $"{title} working with {string.Join(", ", tech)} and distributed systems.",
        Technologies: tech, Countries: ["DE"], RemotePolicy: "Remote", Seniority: "Senior",
        EmploymentType: "FullTime", CompanyStage: "SeriesB", AiUsage: null, SalaryMin: null, SalaryMax: null,
        SalaryCurrency: null, Score: score, PostedAt: null, FirstSeenAt: 1_719_820_800, Status: status,
        ApplicationStatus: null);

    [RequiresDockerFact]
    public async Task Search_returns_relevance_ordered_matches_with_recognisable_fields()
    {
        var result = await _service.SearchAsync(new SearchQuery { Text = "engineer" });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Hits.ShouldNotBeEmpty();
        // Sorted by score descending — the top live hit is the highest-scored one.
        result.Value.Hits[0].Document.Score.ShouldBe(95);
        result.Value.Hits[0].Document.Title.ShouldBe("Staff Backend Engineer");
        result.Value.Hits[0].Document.CompanyName.ShouldBe("Snowflake");
    }

    [RequiresDockerFact]
    public async Task Search_with_a_technology_filter_narrows_and_reports_facet_counts()
    {
        var result = await _service.SearchAsync(new SearchQuery { Technologies = ["Kafka"] });

        result.IsSuccess.ShouldBeTrue();
        // Two live Kafka jobs; the closed one is excluded by default.
        result.Value.Hits.Count.ShouldBe(2);
        result.Value.Hits.ShouldAllBe(h => h.Document.Technologies.Contains("Kafka"));
        result.Value.Facets.ShouldContainKey("technologies");
        result.Value.Facets.ShouldContainKey("companyStage");
    }

    [RequiresDockerFact]
    public async Task Search_with_a_misspelled_technology_still_returns_the_intended_match()
    {
        // "kubernetis" — a typo for Kubernetes — should still find the platform role (AC-03).
        var result = await _service.SearchAsync(new SearchQuery { Text = "kubernetis" });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Hits.ShouldContain(h => h.Document.Title == "Senior Platform Engineer");
    }

    [RequiresDockerFact]
    public async Task Closed_jobs_are_excluded_by_default_and_included_only_on_request()
    {
        var live = await _service.SearchAsync(new SearchQuery { Technologies = ["Kafka"] });
        var all = await _service.SearchAsync(new SearchQuery { Technologies = ["Kafka"], IncludeClosed = true });

        live.Value.Hits.ShouldNotContain(h => h.Document.Status == "Closed");
        all.Value.Hits.ShouldContain(h => h.Document.Status == "Closed");
    }

    [RequiresDockerFact]
    public async Task A_full_page_yields_a_cursor_that_fetches_the_next_page()
    {
        var first = await _service.SearchAsync(new SearchQuery { Text = "engineer", Limit = 2 });

        first.Value.Hits.Count.ShouldBe(2);
        first.Value.NextCursor.ShouldNotBeNull();

        var second = await _service.SearchAsync(
            new SearchQuery { Text = "engineer", Limit = 2, Cursor = first.Value.NextCursor });

        second.IsSuccess.ShouldBeTrue();
        // The second page is strictly below the first page's lowest score — no overlap.
        var firstLowest = first.Value.Hits[^1].Document.Score;
        second.Value.Hits.ShouldAllBe(h => h.Document.Score < firstLowest);
    }
}
