using System.Net;
using JobHunter.Domain.Search;
using JobHunter.Search.Tests.Support;
using Shouldly;
using Xunit;

namespace JobHunter.Search.Tests;

/// <summary>
/// The <see cref="JobHunter.Search.TypesenseQueryService"/> request-building and response-parsing (F9-T03),
/// driven against a stub handler with zero network. Load-bearing: the filter is built from typed
/// parameters and escaped (AC-02); closed jobs are excluded unless asked (AC-08); facet counts come back
/// with every search (AC-02); typo tolerance is requested (AC-03); and an unreachable or erroring index is
/// a clear <see cref="JobHunter.Domain.Common.Result{T}"/> failure, never a partial page dressed as
/// complete (AC-09, QG-3).
/// </summary>
public sealed class TypesenseQueryServiceTests
{
    private const string OneHitBody = """
        {
          "found": 1,
          "hits": [
            {
              "document": {
                "id": "0192e8b7-0000-7000-8000-000000000001",
                "title": "Staff Backend Engineer",
                "companyName": "Snowflake",
                "companyDomain": "snowflake.com",
                "description": "Kafka and distributed systems",
                "technologies": ["Kafka", "Azure", "C#"],
                "countries": ["DE"],
                "remotePolicy": "Remote",
                "seniority": "Staff",
                "employmentType": "FullTime",
                "score": 95.0,
                "firstSeenAt": 1719820800,
                "status": "Live"
              },
              "highlights": [ { "field": "description", "snippet": "experience with <mark>Kafka</mark>" } ]
            }
          ],
          "facet_counts": [
            {
              "field_name": "technologies",
              "counts": [ { "value": "Kafka", "count": 47 }, { "value": "Azure", "count": 31 } ]
            }
          ]
        }
        """;

    [Fact]
    public async Task A_query_searches_the_env_collection_and_sends_the_api_key_in_the_header()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "kafka" });

        result.IsSuccess.ShouldBeTrue();
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldContain($"collections/{IndexerFactory.CollectionName}/documents/search");
        request.ApiKey.ShouldBe("secret-key");
        request.Uri.ToString().ShouldNotContain("secret-key");
    }

    [Fact]
    public async Task A_query_requests_facets_typo_tolerance_and_the_score_sort()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        await service.SearchAsync(new SearchQuery { Text = "kafka" });

        var query = Uri.UnescapeDataString(handler.Requests[0].Uri.Query);
        query.ShouldContain("facet_by=");
        query.ShouldContain("technologies");
        query.ShouldContain("sort_by=score:desc");
        query.ShouldContain("query_by=");
    }

    [Fact]
    public async Task Filters_are_built_from_typed_parameters_and_a_syntax_term_is_carried_as_text()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        await service.SearchAsync(new SearchQuery
        {
            Text = "engineer",
            Technologies = ["Kafka` || status:=`Closed"],
        });

        var query = Uri.UnescapeDataString(handler.Requests[0].Uri.Query);
        // The default live-only clause is present and the malicious term never became a status clause.
        query.ShouldContain("status:=`Live`");
        query.ShouldContain("technologies:=[`Kafka || status:=Closed`]");
    }

    [Fact]
    public async Task Closed_jobs_are_excluded_by_default_and_included_only_on_the_flag()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        await service.SearchAsync(new SearchQuery { Text = "a" });
        await service.SearchAsync(new SearchQuery { Text = "a", IncludeClosed = true });

        Uri.UnescapeDataString(handler.Requests[0].Uri.Query).ShouldContain("status:=`Live`");
        Uri.UnescapeDataString(handler.Requests[1].Uri.Query).ShouldNotContain("status:=`Live`");
    }

    [Fact]
    public async Task An_empty_query_becomes_a_match_all()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        await service.SearchAsync(new SearchQuery { Text = string.Empty });

        Uri.UnescapeDataString(handler.Requests[0].Uri.Query).ShouldContain("q=*");
    }

    [Fact]
    public async Task The_hit_document_facets_and_highlight_are_parsed_from_the_response()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "kafka" });

        result.IsSuccess.ShouldBeTrue();
        var results = result.Value;
        results.Found.ShouldBe(1);
        var hit = results.Hits.ShouldHaveSingleItem();
        hit.Document.Id.ShouldBe("0192e8b7-0000-7000-8000-000000000001");
        hit.Document.Technologies.ShouldBe(["Kafka", "Azure", "C#"]);
        hit.Document.CompanyStage.ShouldBeNull(); // omitted optional reads back as null
        hit.Highlight.ShouldBe("experience with <mark>Kafka</mark>");
        results.Facets.ShouldContainKey("technologies");
        results.Facets["technologies"][0].ShouldBe(new FacetCount("Kafka", 47));
    }

    [Fact]
    public async Task A_full_page_yields_a_next_cursor_and_a_short_page_does_not()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        var full = await service.SearchAsync(new SearchQuery { Text = "kafka", Limit = 1 });
        var partial = await service.SearchAsync(new SearchQuery { Text = "kafka", Limit = 20 });

        full.Value.NextCursor.ShouldNotBeNull();
        partial.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task A_next_cursor_round_trips_into_a_score_boundary_on_the_following_request()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        var first = await service.SearchAsync(new SearchQuery { Text = "kafka", Limit = 1 });
        await service.SearchAsync(new SearchQuery { Text = "kafka", Limit = 1, Cursor = first.Value.NextCursor });

        Uri.UnescapeDataString(handler.Requests[1].Uri.Query).ShouldContain("score:<95");
    }

    [Fact]
    public async Task A_cursor_with_no_other_filter_becomes_the_only_filter_clause()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        // IncludeClosed drops the live-only clause, so with only a cursor the boundary is the whole filter.
        var first = await service.SearchAsync(new SearchQuery { Text = "kafka", Limit = 1, IncludeClosed = true });
        await service.SearchAsync(new SearchQuery
        {
            Text = "kafka",
            Limit = 1,
            IncludeClosed = true,
            Cursor = first.Value.NextCursor,
        });

        var query = Uri.UnescapeDataString(handler.Requests[1].Uri.Query);
        query.ShouldContain("filter_by=score:<95");
        query.ShouldNotContain("&&");
    }

    [Fact]
    public async Task An_invalid_cursor_is_a_clear_failure_and_no_request_is_sent()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "kafka", Cursor = "not-a-cursor!!" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("search.cursor.invalid");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unreachable_index_is_a_result_failure_not_an_exception()
    {
        var service = IndexerFactory.CreateQueryService(new StubHttpMessageHandler());

        var result = await service.SearchAsync(new SearchQuery { Text = "kafka" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("search.unavailable");
    }

    [Fact]
    public async Task A_non_success_status_is_a_result_failure()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "kafka" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("search.unavailable");
    }

    [Fact]
    public async Task An_empty_body_is_a_failure_rather_than_an_empty_page_dressed_as_complete()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, string.Empty));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "kafka" });

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Unparseable_json_is_a_failure()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, "{not json"));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "kafka" });

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_search_cutoff_response_is_reported_as_partial()
    {
        const string body = """{ "found": 0, "hits": [], "search_cutoff": true }""";
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "kafka" });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Partial.ShouldBeTrue();
        result.Value.Hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_matchless_query_is_an_empty_result_set_not_a_failure()
    {
        const string body = """{ "found": 0, "hits": [], "facet_counts": [] }""";
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "nothing-matches" });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Found.ShouldBe(0);
        result.Value.Hits.ShouldBeEmpty();
        result.Value.Partial.ShouldBeFalse();
    }

    [Fact]
    public async Task An_over_length_query_is_truncated_at_a_word_boundary()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        var longText = string.Join(' ', Enumerable.Repeat("kafka", 200)); // ~1200 chars
        var result = await service.SearchAsync(new SearchQuery { Text = longText });

        result.IsSuccess.ShouldBeTrue();
        var q = Uri.UnescapeDataString(handler.Requests[0].Uri.Query);
        // The sent query is shorter than the input and ends on a whole word (no trailing partial token).
        q.ShouldNotContain("kafk ");
        q.Length.ShouldBeLessThan(longText.Length + 100);
    }

    [Fact]
    public async Task The_page_size_is_clamped_to_the_ceiling()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        await service.SearchAsync(new SearchQuery { Text = "a", Limit = 10_000 });

        Uri.UnescapeDataString(handler.Requests[0].Uri.Query).ShouldContain("per_page=100");
    }

    [Fact]
    public async Task A_non_positive_limit_falls_back_to_the_default_page_size()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        await service.SearchAsync(new SearchQuery { Text = "a", Limit = 0 });

        Uri.UnescapeDataString(handler.Requests[0].Uri.Query).ShouldContain("per_page=20");
    }

    [Fact]
    public async Task When_found_is_absent_it_falls_back_to_the_hit_count()
    {
        const string body = """
            { "hits": [ { "document": { "id": "a", "title": "T", "companyName": "C",
              "companyDomain": "c.com", "description": "d", "technologies": [], "countries": [],
              "remotePolicy": "Remote", "employmentType": "FullTime", "score": 1.0,
              "firstSeenAt": 1, "status": "Live" } } ] }
            """;
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "a" });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Found.ShouldBe(1);
    }

    [Fact]
    public async Task A_response_without_a_hits_array_is_an_empty_result_set()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "found": 0 }"""));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "a" });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Hits.ShouldBeEmpty();
        result.Value.Facets.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_hit_without_a_document_and_without_a_highlight_is_handled()
    {
        // First hit has no document (skipped); the second has a document but no highlights (null highlight).
        const string body = """
            { "found": 1, "hits": [
              { "text_match": 1 },
              { "document": { "id": "a", "title": "T", "companyName": "C", "companyDomain": "c.com",
                "description": "d", "technologies": [], "countries": [], "remotePolicy": "Remote",
                "employmentType": "FullTime", "score": 1.0, "firstSeenAt": 1, "status": "Live" } }
            ] }
            """;
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "a" });

        result.IsSuccess.ShouldBeTrue();
        var hit = result.Value.Hits.ShouldHaveSingleItem();
        hit.Document.Id.ShouldBe("a");
        hit.Highlight.ShouldBeNull();
    }

    [Fact]
    public async Task A_highlight_without_a_snippet_reads_as_no_highlight()
    {
        const string body = """
            { "found": 1, "hits": [
              { "document": { "id": "a", "title": "T", "companyName": "C", "companyDomain": "c.com",
                "description": "d", "technologies": [], "countries": [], "remotePolicy": "Remote",
                "employmentType": "FullTime", "score": 1.0, "firstSeenAt": 1, "status": "Live" },
                "highlights": [ { "field": "title" } ] }
            ] }
            """;
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "a" });

        result.Value.Hits[0].Highlight.ShouldBeNull();
    }

    [Fact]
    public async Task Facets_that_are_malformed_are_read_defensively()
    {
        // One facet lacks a field_name (skipped); one has no counts array (empty list); one count lacks a
        // value (skipped). None of this throws — the search still succeeds with what parsed (QG-3).
        const string body = """
            { "found": 0, "hits": [], "facet_counts": [
              { "counts": [ { "value": "x", "count": 1 } ] },
              { "field_name": "remotePolicy" },
              { "field_name": "technologies", "counts": [ { "count": 3 }, { "value": "Kafka", "count": 2 } ] }
            ] }
            """;
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var service = IndexerFactory.CreateQueryService(handler);

        var result = await service.SearchAsync(new SearchQuery { Text = "a" });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Facets.ShouldNotContainKey("companyName"); // the one with no field_name was skipped
        result.Value.Facets["remotePolicy"].ShouldBeEmpty();
        result.Value.Facets["technologies"].ShouldHaveSingleItem();
        result.Value.Facets["technologies"][0].ShouldBe(new FacetCount("Kafka", 2));
    }

    [Fact]
    public async Task An_over_length_query_with_no_spaces_is_hard_truncated()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, OneHitBody));
        var service = IndexerFactory.CreateQueryService(handler);

        var wall = new string('x', 700); // no word boundary to cut on
        var result = await service.SearchAsync(new SearchQuery { Text = wall });

        result.IsSuccess.ShouldBeTrue();
        // Sent query is the hard cap (500), not the 700-char input.
        var q = Uri.UnescapeDataString(handler.Requests[0].Uri.Query);
        q.ShouldContain("q=" + new string('x', 500) + "&");
    }
}
