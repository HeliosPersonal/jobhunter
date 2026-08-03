using JobHunter.Application.Search;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Search;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Search;

/// <summary>
/// The one-command full rebuild (F9-T08, AC-10, QG-1): drop the collection, recreate it empty, stream
/// every live projection through the same pure allowlist the indexer uses and upsert it in batches. The
/// load-bearing behaviours: the rebuild reconstructs document-by-document (not merely a matching count),
/// so the same jobs that went in come back out as byte-identical documents; it takes the maintenance gate
/// for its whole duration so a concurrent reconcile skips; and it reports how long it took so the
/// ten-minute budget is observable.
/// </summary>
public sealed class IndexRebuildServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 4, 0, 0, TimeSpan.Zero);

    private readonly IJobProjectionSource _source = Substitute.For<IJobProjectionSource>();
    private readonly ISearchIndex _index = Substitute.For<ISearchIndex>();
    private readonly IndexMaintenanceGate _gate = new();
    private readonly FakeClock _clock = new(Now);

    public IndexRebuildServiceTests()
    {
        // Default happy-path stubs; a test that needs a failure overrides these afterwards (last Returns wins).
        _index.DropAndRecreateAsync(Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        _index.UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<int>.Success(((IReadOnlyList<JobDocument>)ci[0]!).Count));
    }

    private IndexRebuildService CreateService(int batchSize = 200)
    {
        var options = Options.Create(new ReconcileOptions { BatchSize = batchSize });
        return new IndexRebuildService(_source, _index, _gate, _clock, options, NullLogger<IndexRebuildService>.Instance);
    }

    private static JobProjectionSource Source(int i) => new()
    {
        Id = Guid.Parse($"0192e8b7-0000-7000-8000-{i:D12}"),
        Title = $"Engineer {i}",
        Description = $"desc {i}",
        Status = "Live",
        CompanyName = "Acme",
        CompanyDomain = "acme.com",
        Technologies = ["C#", ".NET"],
        Countries = ["Germany"],
        RemotePolicy = "Remote",
        EmploymentType = "FullTime",
        FirstSeenAt = Now.AddDays(-i),
    };

    private void GivenLiveJobs(params JobProjectionSource[] rows) =>
        _source.ProjectLiveAsync(Arg.Any<CancellationToken>()).Returns(ToAsync(rows));

    private static async IAsyncEnumerable<JobProjectionSource> ToAsync(IEnumerable<JobProjectionSource> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task A_rebuild_drops_the_collection_then_streams_every_live_job()
    {
        GivenLiveJobs(Source(1), Source(2), Source(3));

        var result = await CreateService().RebuildAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Documents.ShouldBe(3);
        await _index.Received(1).DropAndRecreateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rebuild_reconstructs_each_document_from_the_same_pure_projection()
    {
        var rows = new[] { Source(1), Source(2) };
        GivenLiveJobs(rows);

        var captured = new List<JobDocument>();
        await _index.UpsertManyAsync(
            Arg.Do<IReadOnlyList<JobDocument>>(batch => captured.AddRange(batch)), Arg.Any<CancellationToken>());

        await CreateService().RebuildAsync(CancellationToken.None);

        // Document-by-document equivalence, not merely a count (QG-1): every rebuilt document equals the
        // document the pure projection produces from the same source row.
        captured.Count.ShouldBe(2);
        for (var i = 0; i < rows.Length; i++)
        {
            captured[i].ShouldBe(JobDocumentProjection.ToDocument(rows[i]));
        }
    }

    [Fact]
    public async Task A_rebuild_upserts_in_batches_of_the_configured_size()
    {
        GivenLiveJobs(Source(1), Source(2), Source(3), Source(4), Source(5));

        var batchSizes = new List<int>();
        await _index.UpsertManyAsync(
            Arg.Do<IReadOnlyList<JobDocument>>(batch => batchSizes.Add(batch.Count)), Arg.Any<CancellationToken>());

        var result = await CreateService(batchSize: 2).RebuildAsync(CancellationToken.None);

        result.Value.Documents.ShouldBe(5);
        batchSizes.ShouldBe([2, 2, 1]);
    }

    [Fact]
    public async Task A_rebuild_of_an_empty_corpus_recreates_the_collection_and_writes_nothing()
    {
        GivenLiveJobs();

        var result = await CreateService().RebuildAsync(CancellationToken.None);

        result.Value.Documents.ShouldBe(0);
        await _index.Received(1).DropAndRecreateAsync(Arg.Any<CancellationToken>());
        await _index.DidNotReceive().UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rebuild_reports_its_elapsed_time()
    {
        GivenLiveJobs(Source(1));
        // The clock advances between start and finish so the reported duration is non-zero and measured.
        _index.DropAndRecreateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _clock.Advance(TimeSpan.FromMinutes(2));
            return Result<bool>.Success(true);
        });

        var result = await CreateService().RebuildAsync(CancellationToken.None);

        result.Value.Elapsed.ShouldBe(TimeSpan.FromMinutes(2));
        result.Value.WithinBudget.ShouldBeTrue();
    }

    [Fact]
    public async Task A_rebuild_that_overruns_the_budget_reports_it_but_still_succeeds()
    {
        GivenLiveJobs(Source(1));
        _index.DropAndRecreateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _clock.Advance(TimeSpan.FromMinutes(11));
            return Result<bool>.Success(true);
        });

        var result = await CreateService().RebuildAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WithinBudget.ShouldBeFalse();
    }

    [Fact]
    public async Task A_failed_drop_fails_the_rebuild_and_writes_nothing()
    {
        GivenLiveJobs(Source(1));
        _index.DropAndRecreateAsync(Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("search.index.unavailable", "down")));

        var result = await CreateService().RebuildAsync(CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await _index.DidNotReceive().UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_full_upsert_batch_fails_the_rebuild()
    {
        // batchSize 2, 4 rows -> the first full batch flushes inside the loop; that flush fails.
        GivenLiveJobs(Source(1), Source(2), Source(3), Source(4));
        _index.UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(new Error("search.index.unavailable", "down")));

        var result = await CreateService(batchSize: 2).RebuildAsync(CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failed_final_partial_upsert_batch_fails_the_rebuild()
    {
        // batchSize 200, 3 rows -> nothing flushes in the loop; the final partial batch flush fails.
        GivenLiveJobs(Source(1), Source(2), Source(3));
        _index.UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(new Error("search.index.unavailable", "down")));

        var result = await CreateService().RebuildAsync(CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_rebuild_holds_the_gate_while_it_runs_so_a_reconcile_would_skip()
    {
        GivenLiveJobs(Source(1));
        IndexMaintenanceGate.Lease? observed = null;
        _index.DropAndRecreateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            // Mid-rebuild, the gate is held, so a would-be reconcile cannot take it.
            observed = _gate.TryAcquire();
            return Result<bool>.Success(true);
        });

        await CreateService().RebuildAsync(CancellationToken.None);

        observed.ShouldBeNull();
        // After the rebuild the gate is free again for the next operation.
        _gate.TryAcquire().ShouldNotBeNull();
    }

    [Fact]
    public async Task A_rebuild_that_cannot_take_the_gate_skips()
    {
        GivenLiveJobs(Source(1));
        using var _ = _gate.TryAcquire();

        var result = await CreateService().RebuildAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Skipped.ShouldBeTrue();
        await _index.DidNotReceive().DropAndRecreateAsync(Arg.Any<CancellationToken>());
    }
}
