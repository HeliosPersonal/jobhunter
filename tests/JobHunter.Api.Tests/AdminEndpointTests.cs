using System.Net;
using System.Net.Http.Json;
using JobHunter.Api.Endpoints;
using JobHunter.Domain.Common;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The operational endpoints end-to-end (T07): every runbook recovery action as an authenticated,
/// admin-scoped endpoint so recovery never needs database access (AC-07, US-06). Reindex and reprocess
/// enqueue through <see cref="Domain.Abstractions.IOperationScheduler"/> and answer 202 with an operation
/// id rather than blocking; source release answers 200 with the outcome (404 for an unknown id); stats
/// answers the PostgreSQL truth even while the index is down (QG-3). Each route requires
/// <c>jobhunter:admin</c>; a read token is a 403 and an anonymous request a 401 (the endpoint-convention
/// gate). No response carries a CV-derived value, match reason or application note (QG-2).
/// </summary>
public sealed class AdminEndpointTests : IClassFixture<EndpointsHostFactory>
{
    private readonly EndpointsHostFactory _factory;

    public AdminEndpointTests(EndpointsHostFactory factory) => _factory = factory;

    // --- Reindex -----------------------------------------------------------------------------------

    [Fact]
    public async Task Reindex_enqueues_a_rebuild_and_returns_202_with_the_operation_id()
    {
        _factory.Operations.EnqueueReindex().Returns("job-42");

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(new Uri("/api/admin/search/reindex", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        body.ShouldNotBeNull();
        body.OperationId.ShouldBe("job-42");
        body.Status.ShouldBe("Enqueued");
        _factory.Operations.Received(1).EnqueueReindex();
    }

    [Fact]
    public async Task Reindex_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(new Uri("/api/admin/search/reindex", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reindex_without_a_token_is_a_401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(new Uri("/api/admin/search/reindex", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Reprocess ---------------------------------------------------------------------------------

    [Fact]
    public async Task Reprocess_enqueues_with_the_requested_window_and_returns_202()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _factory.Operations.EnqueueReprocess(from).Returns("job-99");

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/jobs/reprocess", UriKind.Relative), new ReprocessRequest(from));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>();
        body!.OperationId.ShouldBe("job-99");
        _factory.Operations.Received(1).EnqueueReprocess(from);
    }

    [Fact]
    public async Task Reprocess_without_a_window_reprocesses_the_full_history()
    {
        _factory.Operations.EnqueueReprocess(DateTimeOffset.MinValue).Returns("job-full");

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/jobs/reprocess", UriKind.Relative), new ReprocessRequest(null));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _factory.Operations.Received(1).EnqueueReprocess(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task Reprocess_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/jobs/reprocess", UriKind.Relative), new ReprocessRequest(null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Unquarantine ------------------------------------------------------------------------------

    [Fact]
    public async Task Unquarantine_releases_a_quarantined_source_and_returns_200()
    {
        var sourceId = Guid.NewGuid();
        var source = QuarantinedSource(sourceId);
        _factory.Sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns(source);

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(
            new Uri($"/api/admin/sources/{sourceId}/unquarantine", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UnquarantineResponse>();
        body!.SourceId.ShouldBe(sourceId);
        body.Outcome.ShouldBe("Released");
        source.QuarantinedUntil.ShouldBeNull();
        await _factory.Sources.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unquarantine_of_a_healthy_source_reports_nothing_to_do_and_does_not_save()
    {
        var sourceId = Guid.NewGuid();
        _factory.Sources.ClearReceivedCalls();
        _factory.Sources.FindAsync(sourceId, Arg.Any<CancellationToken>())
            .Returns(new JobSource(sourceId, Guid.NewGuid(), Guid.NewGuid(), "https://boards.example/acme"));

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(
            new Uri($"/api/admin/sources/{sourceId}/unquarantine", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UnquarantineResponse>();
        body!.Outcome.ShouldBe("NotQuarantined");
        await _factory.Sources.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unquarantine_of_an_unknown_source_is_a_404()
    {
        var sourceId = Guid.NewGuid();
        _factory.Sources.FindAsync(sourceId, Arg.Any<CancellationToken>()).Returns((JobSource?)null);

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(
            new Uri($"/api/admin/sources/{sourceId}/unquarantine", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Unquarantine_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(
            new Uri($"/api/admin/sources/{Guid.NewGuid()}/unquarantine", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Stats -------------------------------------------------------------------------------------

    [Fact]
    public async Task Stats_reports_live_count_indexed_count_and_drift_when_the_index_answers()
    {
        _factory.LiveJobCounter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(100L);
        _factory.Index.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<long>.Success(90L));

        using var client = _factory.OwnerClient("jobhunter:admin");
        var body = await client.GetFromJsonAsync<StatsResponse>(new Uri("/api/admin/stats", UriKind.Relative));

        body.ShouldNotBeNull();
        body.LiveJobs.ShouldBe(100L);
        body.IndexedDocuments.ShouldBe(90L);
        body.IndexDrift.ShouldNotBeNull();
        body.IndexDrift.Value.ShouldBe(0.1d, 0.0001d);
        body.IndexAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task Stats_reports_the_postgres_truth_and_a_null_index_when_the_index_is_down()
    {
        _factory.LiveJobCounter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(100L);
        _factory.Index.CountAsync(Arg.Any<CancellationToken>())
            .Returns(Result<long>.Failure(new Error("index.unavailable", "down")));

        using var client = _factory.OwnerClient("jobhunter:admin");
        var body = await client.GetFromJsonAsync<StatsResponse>(new Uri("/api/admin/stats", UriKind.Relative));

        body!.LiveJobs.ShouldBe(100L);
        body.IndexedDocuments.ShouldBeNull();
        body.IndexDrift.ShouldBeNull();
        body.IndexAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Stats_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/admin/stats", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static JobSource QuarantinedSource(Guid id)
    {
        var source = new JobSource(id, Guid.NewGuid(), Guid.NewGuid(), "https://boards.example/acme");
        var clock = new FakeClock();

        // Two consecutive failures put the source into quarantine (QuarantineThreshold), so the release path
        // has something to lift.
        source.RecordFailure(clock, TimeSpan.FromHours(1));
        source.RecordFailure(clock, TimeSpan.FromHours(1));
        source.QuarantinedUntil.ShouldNotBeNull();
        return source;
    }
}
