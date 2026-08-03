using JobHunter.Application.Search;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Search;
using JobHunter.Search;
using JobHunter.Search.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Search.Tests;

/// <summary>
/// The fault-injection suite (F9-T10, AC-09, QG-3) — <c>SearchUnavailable_ReportsClearly_AndPipelineIsUnaffected</c>.
/// It drives the whole search surface with the index down and asserts the impact is contained to search
/// alone, so a Typesense outage costs the platform its search feature and nothing else. The index is taken
/// down the honest way: the real <see cref="TypesenseIndexer"/> and <see cref="TypesenseQueryService"/>
/// adapters are wired over the transport-failure stub — the same "index unreachable" case the adapters map
/// to a failure value — so this is the production code path, not a re-stubbed port. Zero network, so it
/// runs in every PR rather than only where Docker is present.
///
/// <para>Three claims together are "the rest of the system is unaffected": the read path returns a clear
/// <see cref="Result{T}"/> failure and never throws (a client sees a 503, never a partial page dressed as
/// complete); the write path raises a <see cref="SearchIndexingException"/> that Wolverine contains on the
/// indexer's own queue and never propagates to another stage; and the operator's corpus snapshot — which
/// stands in here for every PostgreSQL-authoritative stage, up to and including the 07:00 digest that never
/// touches Typesense — still answers from PostgreSQL, reporting the index as unavailable rather than failing
/// with it (the "delivered digest with the index down", test-plan §NFR).</para>
/// </summary>
public sealed class FaultInjectionTests
{
    private static readonly Guid JobId = Guid.Parse("0192e8b7-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset Occurred = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private static JobProjectionSource SourceRow() => new()
    {
        Id = JobId,
        Title = "Staff Backend Engineer",
        Description = "distributed systems",
        Status = "Live",
        CompanyName = "Acme",
        CompanyDomain = "acme.com",
        RemotePolicy = "Remote",
        EmploymentType = "FullTime",
        FirstSeenAt = Occurred,
    };

    [Fact]
    public async Task Search_unavailable_reports_clearly_and_the_pipeline_is_unaffected()
    {
        // The index is down: the transport-failure stub (null responder) throws on every request, exactly
        // the "index unreachable" case the adapters are built to absorb. Both real adapters share it.
        var downHandler = new StubHttpMessageHandler();
        var query = IndexerFactory.CreateQueryService(downHandler);
        var index = IndexerFactory.Create(downHandler);

        // 1. The read path — the client-facing surface — is a clear failure value, never an exception and
        //    never an empty page pretending to be complete (AC-09).
        var read = await query.SearchAsync(new SearchQuery { Text = "kafka" });

        read.IsFailure.ShouldBeTrue();
        read.Error.Code.ShouldBe("search.unavailable");

        // 2. The write path (the indexing stage) surfaces the outage as a SearchIndexingException, which
        //    Wolverine retries then dead-letters on the indexer's own queue — contained, never thrown into
        //    another stage. The projection source is a zero-network fake; the failure comes from the index.
        var projectionSource = Substitute.For<IJobProjectionSource>();
        projectionSource.ProjectAsync(JobId, Arg.Any<CancellationToken>()).Returns(SourceRow());
        var indexingHandler = new SearchIndexingHandler(projectionSource, index, NullLogger<SearchIndexingHandler>.Instance);

        var indexing = await Should.ThrowAsync<SearchIndexingException>(() => indexingHandler.Handle(
            new JobIndexRequested(JobId, JobIndexRequested.Upsert, Occurred), CancellationToken.None));
        indexing.JobId.ShouldBe(JobId);
        indexing.ErrorCode.ShouldBe("search.index.unavailable");

        // 3. A PostgreSQL-authoritative stage still delivers with the index down — the corpus snapshot stands
        //    in for the whole system-of-record side, up to the 07:00 digest that never touches Typesense. The
        //    live-job count is authoritative and reported; the index is simply flagged unavailable, not fatal.
        var liveJobCounter = Substitute.For<ILiveJobCounter>();
        liveJobCounter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(1_234L);
        var stats = new CorpusStatsService(liveJobCounter, index, NullLogger<CorpusStatsService>.Instance);

        var snapshot = await stats.CollectAsync();

        snapshot.LiveJobs.ShouldBe(1_234L);       // the PostgreSQL truth is delivered regardless
        snapshot.IndexAvailable.ShouldBeFalse();  // the outage is reported plainly, not fabricated away
        snapshot.IndexedDocuments.ShouldBeNull();
        snapshot.Drift.ShouldBeNull();
    }
}
