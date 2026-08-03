using System.Net;
using JobHunter.Domain.Search;
using JobHunter.Search.Tests.Support;
using Shouldly;
using Xunit;

namespace JobHunter.Search.Tests;

/// <summary>
/// The <see cref="JobHunter.Search.TypesenseIndexer"/> outcome mapping (F9-T02), driven against a stub
/// handler with zero network. The load-bearing rules: an unavailable index is a failed
/// <see cref="JobHunter.Domain.Common.Result{T}"/> and never an exception (QG-3); the upsert targets the
/// <c>{env}_jobhunter_jobs</c> collection with <c>action=upsert</c> keyed by the job id (SAD §8);
/// EnsureCollection treats a 409 as success (idempotent); delete treats a 404 as success (idempotent under
/// redelivery); and the api key travels in the header, never in a log or the path (invariant 12).
/// </summary>
public sealed class TypesenseIndexerTests
{
    private static JobDocument Document(string id = "0192e8b7-0000-7000-8000-000000000001") => new(
        Id: id, Title: "Engineer", CompanyName: "Acme", CompanyDomain: "acme.com", Description: "d",
        Technologies: ["C#"], Countries: ["DE"], RemotePolicy: "Remote", Seniority: "Senior",
        EmploymentType: "FullTime", CompanyStage: null, AiUsage: null, SalaryMin: null, SalaryMax: null,
        SalaryCurrency: null, Score: 10, PostedAt: null, FirstSeenAt: 1_719_820_800, Status: "Live",
        ApplicationStatus: null);

    [Fact]
    public async Task Upsert_targets_the_env_collection_with_action_upsert_and_sends_the_api_key_in_the_header()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.UpsertAsync(Document());

        result.IsSuccess.ShouldBeTrue();
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsolutePath.ShouldContain($"collections/{IndexerFactory.CollectionName}/documents");
        request.Uri.Query.ShouldContain("action=upsert");
        request.ApiKey.ShouldBe("secret-key");
        // The api key is a header, never part of the path.
        request.Uri.ToString().ShouldNotContain("secret-key");
    }

    [Fact]
    public async Task An_unreachable_index_is_a_result_failure_not_an_exception()
    {
        // A null responder throws HttpRequestException from the transport — the "index down" case.
        var indexer = IndexerFactory.Create(new StubHttpMessageHandler());

        var result = await indexer.UpsertAsync(Document());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("search.index.unavailable");
    }

    [Fact]
    public async Task A_non_success_status_is_a_result_failure()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.UpsertAsync(Document());

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Ensure_collection_treats_a_409_conflict_as_success_so_it_is_idempotent()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.Conflict));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.EnsureCollectionAsync();

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Ensure_collection_posts_the_schema_with_the_env_collection_name()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.Created));
        var indexer = IndexerFactory.Create(handler);

        await indexer.EnsureCollectionAsync();

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldEndWith("collections");
        request.Body.ShouldContain(IndexerFactory.CollectionName);
        request.Body.ShouldContain("token_separators");
    }

    [Fact]
    public async Task Deleting_an_absent_document_is_a_success_so_the_delete_is_idempotent()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.NotFound));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.DeleteAsync(Guid.Parse("0192e8b7-0000-7000-8000-000000000001"));

        result.IsSuccess.ShouldBeTrue();
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Delete);
        request.Uri.AbsolutePath.ShouldContain("0192e8b7-0000-7000-8000-000000000001");
    }

    [Fact]
    public async Task Two_racing_upserts_of_the_same_job_send_the_same_document_id()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK));
        var indexer = IndexerFactory.Create(handler);

        await indexer.UpsertAsync(Document());
        await indexer.UpsertAsync(Document());

        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].Body.ShouldContain("0192e8b7-0000-7000-8000-000000000001");
        handler.Requests[1].Body.ShouldBe(handler.Requests[0].Body);
    }

    [Fact]
    public async Task Count_reads_the_num_documents_field()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"num_documents\": 42}"));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.CountAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Upsert_many_counts_the_import_lines_that_report_success()
    {
        const string importResponse = "{\"success\":true}\n{\"success\":false,\"error\":\"x\"}\n{\"success\":true}";
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, importResponse));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.UpsertManyAsync([Document("a"), Document("b"), Document("c")]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(2);
    }

    [Fact]
    public async Task Count_with_a_body_missing_num_documents_reads_zero()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.CountAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }

    [Fact]
    public async Task Count_with_an_empty_body_reads_zero()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, string.Empty));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.CountAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }

    [Fact]
    public async Task Upsert_many_with_an_empty_import_response_counts_every_requested_document()
    {
        // Some Typesense responses to a successful import are empty; treat that as "all succeeded".
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, string.Empty));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.UpsertManyAsync([Document("a"), Document("b")]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(2);
    }

    [Fact]
    public async Task Upsert_many_does_not_count_a_line_that_lacks_a_success_field()
    {
        const string importResponse = "{\"success\":true}\n{\"code\":400}";
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, importResponse));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.UpsertManyAsync([Document("a"), Document("b")]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(1);
    }

    [Fact]
    public async Task Upsert_many_ignores_an_unparseable_import_line()
    {
        const string importResponse = "{\"success\":true}\nnot-json";
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, importResponse));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.UpsertManyAsync([Document("a"), Document("b")]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(1);
    }

    [Fact]
    public async Task Upsert_many_with_no_documents_is_a_success_with_zero_and_no_request()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.UpsertManyAsync([]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Drop_and_recreate_drops_then_recreates_the_collection()
    {
        var handler = new StubHttpMessageHandler((request, _) => request.Method == HttpMethod.Delete
            ? StubHttpMessageHandler.Json(HttpStatusCode.OK)
            : StubHttpMessageHandler.Json(HttpStatusCode.Created));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.DropAndRecreateAsync();

        result.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Delete);
        handler.Requests[1].Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task Drop_and_recreate_that_cannot_drop_reports_failure_without_recreating()
    {
        var handler = new StubHttpMessageHandler((request, _) => request.Method == HttpMethod.Delete
            ? StubHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable)
            : StubHttpMessageHandler.Json(HttpStatusCode.Created));
        var indexer = IndexerFactory.Create(handler);

        var result = await indexer.DropAndRecreateAsync();

        result.IsFailure.ShouldBeTrue();
        handler.Requests.ShouldHaveSingleItem();
    }
}
