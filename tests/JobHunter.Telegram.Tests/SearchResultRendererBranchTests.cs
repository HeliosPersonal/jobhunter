using JobHunter.Domain.Search;
using JobHunter.Telegram.Search;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests;

/// <summary>
/// The remaining decision arms of the <c>/search</c> renderer (F9-T09): every rung of the
/// most-restrictive-filter ladder the empty-results advice climbs — a numeric threshold cuts hardest, then a
/// date window, then each typed set in catalogue order — the empty-query variant of that advice, and the
/// facet arm that surfaces nothing when the leading facet value is blank. Assertion-based, so the committed
/// rendering-corpus snapshots stay untouched.
/// </summary>
public sealed class SearchResultRendererBranchTests
{
    [Fact]
    public void An_empty_result_with_a_salary_floor_names_it_to_drop_over_the_typed_sets()
    {
        var query = new SearchQuery { Text = "kafka", SalaryMin = 120_000, Technologies = ["go"] };

        var rendered = SearchResultRenderer.Render("min-salary:120000 tech:go kafka", Empty(), query);

        // A salary floor cuts harder than any typed set, so it is the one named to drop. The hyphen in the
        // token is MarkdownV2-escaped, so the assertion checks the escaped form.
        rendered.ShouldContain(@"min\-salary:");
        rendered.ShouldContain("drop", Case.Insensitive);
    }

    [Fact]
    public void An_empty_result_with_a_date_window_names_since_over_the_typed_sets()
    {
        var query = new SearchQuery { Text = "kafka", PostedAfter = 1_700_000_000, Technologies = ["go"] };

        var rendered = SearchResultRenderer.Render("since:30d tech:go kafka", Empty(), query);

        // A date window cuts harder than a typed set but softer than a numeric threshold.
        rendered.ShouldContain("since:");
    }

    [Fact]
    public void An_empty_result_with_only_a_stage_filter_names_the_stage_token()
    {
        var query = new SearchQuery { Text = "kafka", CompanyStages = ["SeriesB"] };

        var rendered = SearchResultRenderer.Render("stage:SeriesB kafka", Empty(), query);

        rendered.ShouldContain("stage:");
    }

    [Fact]
    public void An_empty_result_with_only_a_country_filter_names_the_country_token()
    {
        var query = new SearchQuery { Text = "kafka", Countries = ["DE"] };

        var rendered = SearchResultRenderer.Render("country:DE kafka", Empty(), query);

        rendered.ShouldContain("country:");
    }

    [Fact]
    public void An_empty_result_with_only_a_remote_filter_names_the_remote_token()
    {
        var query = new SearchQuery { Text = "kafka", RemotePolicies = ["remote"] };

        var rendered = SearchResultRenderer.Render("remote:remote kafka", Empty(), query);

        rendered.ShouldContain("remote:");
    }

    [Fact]
    public void An_empty_result_with_only_a_seniority_filter_names_the_seniority_token()
    {
        var query = new SearchQuery { Text = "kafka", Seniorities = ["staff"] };

        var rendered = SearchResultRenderer.Render("seniority:staff kafka", Empty(), query);

        rendered.ShouldContain("seniority:");
    }

    [Fact]
    public void An_empty_query_with_filters_still_names_the_most_restrictive_one_to_drop()
    {
        // The raw query is only whitespace, so the drop-advice line takes its no-quoted-text form.
        var query = new SearchQuery { Text = string.Empty, MinScore = 90 };

        var rendered = SearchResultRenderer.Render("   ", Empty(), query);

        rendered.ShouldContain("drop", Case.Insensitive);
        rendered.ShouldContain("min:");
        // With no query to echo, the advice never quotes a phrase.
        rendered.ShouldNotContain("\"");
    }

    [Fact]
    public void Facets_present_but_with_blank_leading_values_surface_no_refine_line()
    {
        var facets = new Dictionary<string, IReadOnlyList<FacetCount>>
        {
            ["technologies"] = [new FacetCount("   ", 12)],
            ["companyStage"] = [new FacetCount("", 8)],
        };
        var results = ResultsWithFacets(found: 20, facets, Doc("Backend Engineer", "Acme", ["Germany"]));

        var rendered = SearchResultRenderer.Render("backend", results);

        // Every leading facet value is blank, so no paste-back token can be formed — the Refine line is dropped.
        rendered.ShouldNotContain("Refine");
    }

    [Fact]
    public void A_facet_field_with_no_counts_is_skipped_while_a_populated_one_still_shows()
    {
        var facets = new Dictionary<string, IReadOnlyList<FacetCount>>
        {
            ["technologies"] = [],
            ["countries"] = [new FacetCount("DE", 6)],
        };
        var results = ResultsWithFacets(found: 20, facets, Doc("Backend Engineer", "Acme", ["Germany"]));

        var rendered = SearchResultRenderer.Render("backend", results);

        // The empty technologies facet contributes nothing; the populated countries facet still renders.
        rendered.ShouldContain("Refine");
        rendered.ShouldContain("country:DE");
        rendered.ShouldNotContain("tech:");
    }

    private static SearchResults Empty() =>
        new([], 0, new Dictionary<string, IReadOnlyList<FacetCount>>(), NextCursor: null, Partial: false);

    private static SearchResults ResultsWithFacets(
        int found, IReadOnlyDictionary<string, IReadOnlyList<FacetCount>> facets, params JobDocument[] docs) =>
        new(
            docs.Select(d => new SearchHit(d, Highlight: null)).ToList(),
            found,
            facets,
            NextCursor: null,
            Partial: false);

    private static JobDocument Doc(string title, string company, IReadOnlyList<string> countries) =>
        new(
            Id: Guid.NewGuid().ToString(),
            Title: title,
            CompanyName: company,
            CompanyDomain: "acme.com",
            Description: "A role.",
            Technologies: ["C#"],
            Countries: countries,
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
}
