using JobHunter.Domain.Search;
using JobHunter.Telegram.Search;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests;

/// <summary>
/// The digest card layout for <c>/search</c> results (F9-T09, AC-11). The load-bearing behaviours: no
/// results is a helpful "broaden your query" message rather than an empty response; the total-found count
/// is shown when there is more than the ten-card cap; every dynamic value is MarkdownV2-escaped (the F5
/// escaping rule); the F4-owned score is omitted while un-ranked (0) rather than shown as a real zero (the
/// decoupling decision); and a partial page is labelled, never presented as complete (QG-3).
/// </summary>
public sealed class SearchResultRendererTests
{
    [Fact]
    public void No_results_suggests_a_broader_query_rather_than_an_empty_message()
    {
        var rendered = SearchResultRenderer.Render("kubernetes on mars", Empty());

        rendered.ShouldNotBeNullOrWhiteSpace();
        rendered.ShouldContain("No results");
        rendered.ShouldContain("broader", Case.Insensitive);
    }

    [Fact]
    public void A_single_result_renders_a_card_with_title_company_and_salary()
    {
        var results = Results(
            found: 1,
            partial: false,
            Doc("Senior Platform Engineer", "Stripe", stage: "Series-public", countries: ["Ireland"],
                salaryMin: 150000, salaryMax: 190000, currency: "EUR", score: 87));

        var rendered = SearchResultRenderer.Render("platform", results);

        rendered.ShouldContain("Senior Platform Engineer");
        rendered.ShouldContain("Stripe");
        rendered.ShouldContain("150k");
        rendered.ShouldContain("190k");
        rendered.ShouldContain("87");
        // The company line's hyphen in "Series-public" must be MarkdownV2-escaped.
        rendered.ShouldContain(@"Series\-public");
    }

    [Fact]
    public void The_total_found_count_is_shown_when_it_exceeds_the_shown_cards()
    {
        var results = Results(found: 42, partial: false,
            Doc("Backend Engineer", "Acme", countries: ["Germany"]));

        var rendered = SearchResultRenderer.Render("backend", results);

        rendered.ShouldContain("42");
        rendered.ShouldContain("of");
    }

    [Fact]
    public void An_unranked_score_of_zero_is_omitted_not_shown_as_a_real_zero()
    {
        var results = Results(found: 1, partial: false,
            Doc("Data Engineer", "Acme", countries: ["Remote"], score: 0));

        var rendered = SearchResultRenderer.Render("data", results);

        // The score marker only appears with a real ranking; F4 is not merged so the score is 0 and omitted.
        rendered.ShouldNotContain("🎯");
    }

    [Fact]
    public void A_partial_result_is_labelled_and_never_presented_as_complete()
    {
        var results = Results(found: 3, partial: true,
            Doc("SRE", "Acme", countries: ["Poland"]));

        var rendered = SearchResultRenderer.Render("sre", results);

        rendered.ShouldContain("Partial", Case.Insensitive);
    }

    [Fact]
    public void A_hostile_title_is_escaped_and_displayed_literally()
    {
        var results = Results(found: 1, partial: false,
            Doc("*bold* [link](http://evil)", "Acme", countries: ["Spain"]));

        var rendered = SearchResultRenderer.Render("x", results);

        // The markup characters are backslash-escaped, so none survives as active MarkdownV2.
        rendered.ShouldContain(@"\*bold\*");
        rendered.ShouldContain(@"\[link\]\(http://evil\)");
    }

    [Fact]
    public void A_very_long_title_is_truncated_at_a_word_boundary()
    {
        var longTitle = string.Join(' ', Enumerable.Repeat("Senior", 30));
        var results = Results(found: 1, partial: false, Doc(longTitle, "Acme", countries: ["France"]));

        var rendered = SearchResultRenderer.Render("x", results);

        rendered.ShouldContain("…");
    }

    [Fact]
    public void No_results_for_an_empty_query_suggests_an_example()
    {
        var rendered = SearchResultRenderer.Render("   ", Empty());

        rendered.ShouldContain("No results");
        rendered.ShouldContain("staff backend", Case.Insensitive);
    }

    [Fact]
    public void A_card_with_no_countries_falls_back_to_the_remote_policy_for_location()
    {
        var results = Results(found: 1, partial: false,
            Doc("Backend Engineer", "Acme", countries: []));

        var rendered = SearchResultRenderer.Render("backend", results);

        // No country was indexed, so the location summary is the remote policy.
        rendered.ShouldContain("Remote");
    }

    [Fact]
    public void A_salary_without_a_currency_omits_the_currency_suffix()
    {
        var results = Results(found: 1, partial: false,
            Doc("Engineer", "Acme", countries: ["Italy"], salaryMin: 800, salaryMax: 950, currency: null));

        var rendered = SearchResultRenderer.Render("x", results);

        // Under 1000 is shown verbatim, not in thousands, and no currency code follows.
        rendered.ShouldContain("800");
        rendered.ShouldContain("950");
    }

    [Fact]
    public void A_salary_with_only_a_minimum_is_treated_as_no_range()
    {
        var results = Results(found: 1, partial: false,
            Doc("Engineer", "Acme", countries: ["Finland"], salaryMin: 100000, salaryMax: null));

        var rendered = SearchResultRenderer.Render("x", results);

        // A one-sided salary is not a range, so no salary line is rendered.
        rendered.ShouldNotContain("💰");
    }

    [Fact]
    public void A_single_result_reads_as_singular()
    {
        var results = Results(found: 1, partial: false, Doc("Engineer", "Acme", countries: ["Norway"]));

        var rendered = SearchResultRenderer.Render("x", results);

        rendered.ShouldContain("1 result");
        rendered.ShouldNotContain("1 results");
    }

    [Fact]
    public void A_long_single_word_title_with_no_boundary_is_still_truncated()
    {
        var results = Results(found: 1, partial: false,
            Doc(new string('x', 200), "Acme", countries: ["Sweden"]));

        var rendered = SearchResultRenderer.Render("x", results);

        rendered.ShouldContain("…");
    }

    [Fact]
    public void An_empty_title_renders_without_error()
    {
        var results = Results(found: 1, partial: false, Doc(string.Empty, "Acme", countries: ["Denmark"]));

        var rendered = SearchResultRenderer.Render("x", results);

        rendered.ShouldContain("Acme");
    }

    [Fact]
    public void A_null_query_with_no_results_still_renders_the_empty_message()
    {
        var rendered = SearchResultRenderer.Render(null!, Empty());

        rendered.ShouldContain("No results");
    }

    [Fact]
    public void A_ranked_card_with_no_salary_still_shows_the_score()
    {
        var results = Results(found: 1, partial: false,
            Doc("Engineer", "Acme", countries: ["Portugal"], score: 91));

        var rendered = SearchResultRenderer.Render("x", results);

        rendered.ShouldContain("🎯");
        rendered.ShouldContain("91");
        rendered.ShouldNotContain("💰");
    }

    [Fact]
    public void Several_results_under_the_cap_read_as_plural_without_a_showing_prefix()
    {
        var results = Results(found: 2, partial: false,
            Doc("One", "Acme", countries: ["Austria"]),
            Doc("Two", "Acme", countries: ["Austria"]));

        var rendered = SearchResultRenderer.Render("x", results);

        rendered.ShouldContain("2 results");
        rendered.ShouldNotContain("Showing");
    }

    [Fact]
    public void Render_rejects_a_null_result_set() =>
        Should.Throw<ArgumentNullException>(() => SearchResultRenderer.Render("x", null!));

    [Fact]
    public void The_leading_facets_are_rendered_as_copyable_refinement_tokens()
    {
        var facets = new Dictionary<string, IReadOnlyList<FacetCount>>
        {
            ["technologies"] = [new FacetCount("kafka", 12), new FacetCount("go", 3)],
            ["companyStage"] = [new FacetCount("SeriesB", 8)],
            ["countries"] = [new FacetCount("DE", 6)],
        };
        var results = ResultsWithFacets(found: 20, facets, Doc("Backend Engineer", "Acme", countries: ["Germany"]));

        var rendered = SearchResultRenderer.Render("backend", results);

        // The leading facet values become the catalogue's own filter tokens, ordered by count, so the next
        // query can be narrower (AC-02).
        rendered.ShouldContain("Refine");
        rendered.ShouldContain("tech:kafka");
        rendered.ShouldContain("stage:SeriesB");
        rendered.ShouldContain("country:DE");
    }

    [Fact]
    public void Facets_are_omitted_when_there_are_none()
    {
        var results = Results(found: 1, partial: false, Doc("Backend Engineer", "Acme", countries: ["Germany"]));

        var rendered = SearchResultRenderer.Render("backend", results);

        rendered.ShouldNotContain("Refine");
    }

    [Fact]
    public void An_empty_result_with_filters_suggests_dropping_the_most_restrictive_one()
    {
        var query = new SearchQuery { Text = "kafka", MinScore = 90, Technologies = ["go"] };

        var rendered = SearchResultRenderer.Render("min:90 tech:go kafka", Empty(), query);

        // A numeric score threshold cuts hardest, so it is the one named to drop.
        rendered.ShouldContain("min:");
        rendered.ShouldContain("drop", Case.Insensitive);
    }

    [Fact]
    public void An_empty_result_with_only_set_filters_names_a_set_filter_to_drop()
    {
        var query = new SearchQuery { Text = "kafka", Technologies = ["go"], Countries = ["DE"] };

        var rendered = SearchResultRenderer.Render("tech:go country:DE kafka", Empty(), query);

        rendered.ShouldContain("drop", Case.Insensitive);
        rendered.ShouldContain("tech:");
    }

    [Fact]
    public void An_empty_result_with_no_filters_keeps_the_broaden_message()
    {
        var query = new SearchQuery { Text = "kubernetes on mars" };

        var rendered = SearchResultRenderer.Render("kubernetes on mars", Empty(), query);

        rendered.ShouldContain("broader", Case.Insensitive);
        rendered.ShouldNotContain("drop", Case.Insensitive);
    }

    private static SearchResults Empty() =>
        new([], 0, new Dictionary<string, IReadOnlyList<FacetCount>>(), NextCursor: null, Partial: false);

    private static SearchResults Results(int found, bool partial, params JobDocument[] docs) =>
        new(
            docs.Select(d => new SearchHit(d, Highlight: null)).ToList(),
            found,
            new Dictionary<string, IReadOnlyList<FacetCount>>(),
            NextCursor: null,
            partial);

    private static SearchResults ResultsWithFacets(
        int found, IReadOnlyDictionary<string, IReadOnlyList<FacetCount>> facets, params JobDocument[] docs) =>
        new(
            docs.Select(d => new SearchHit(d, Highlight: null)).ToList(),
            found,
            facets,
            NextCursor: null,
            Partial: false);

    private static JobDocument Doc(
        string title,
        string company,
        string? stage = null,
        IReadOnlyList<string>? countries = null,
        int? salaryMin = null,
        int? salaryMax = null,
        string? currency = null,
        double score = 0d) =>
        new(
            Id: Guid.NewGuid().ToString(),
            Title: title,
            CompanyName: company,
            CompanyDomain: "acme.com",
            Description: "A role.",
            Technologies: ["C#"],
            Countries: countries ?? [],
            RemotePolicy: "Remote",
            Seniority: "Senior",
            EmploymentType: "FullTime",
            CompanyStage: stage,
            AiUsage: null,
            SalaryMin: salaryMin,
            SalaryMax: salaryMax,
            SalaryCurrency: currency,
            Score: score,
            PostedAt: null,
            FirstSeenAt: 1_700_000_000,
            Status: "Live",
            ApplicationStatus: null);
}
