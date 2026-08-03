using System.Diagnostics.Metrics;
using JobHunter.Application.Common;
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
/// The nightly reconcile (F9-T08, AC-10, SAD §6.3): compare the live-job count in PostgreSQL against the
/// document count in the index; when they diverge by more than one percent, re-index the live set and emit
/// <c>jobhunter.index.drift</c> so drift that does not self-heal is visible. The load-bearing behaviours:
/// drift within the threshold is a no-op that still records a low drift value; drift above the threshold
/// re-indexes and records the divergence; and a reconcile that fires during an active rebuild cannot take
/// the maintenance gate and skips.
/// </summary>
public sealed class IndexReconcileServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 4, 0, 0, TimeSpan.Zero);

    private readonly ILiveJobCounter _counter = Substitute.For<ILiveJobCounter>();
    private readonly IJobProjectionSource _source = Substitute.For<IJobProjectionSource>();
    private readonly ISearchIndex _index = Substitute.For<ISearchIndex>();
    private readonly IndexMaintenanceGate _gate = new();
    private readonly FakeClock _clock = new(Now);

    public IndexReconcileServiceTests()
    {
        // Default happy-path stub; a test that needs a failing batch overrides it afterwards.
        _index.UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<int>.Success(((IReadOnlyList<JobDocument>)ci[0]!).Count));
    }

    private IndexReconcileService CreateService(double threshold = 0.01)
    {
        var options = Options.Create(new ReconcileOptions { DriftThreshold = threshold, BatchSize = 200 });
        return new IndexReconcileService(
            _counter, _source, _index, _gate, options, NullLogger<IndexReconcileService>.Instance);
    }

    private void GivenCounts(long liveJobs, long indexedDocuments)
    {
        _counter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(liveJobs);
        _index.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<long>.Success(indexedDocuments));
    }

    private void GivenLiveJobs(int count)
    {
        var rows = Enumerable.Range(0, count).Select(Source).ToArray();
        _source.ProjectLiveAsync(Arg.Any<CancellationToken>()).Returns(_ => ToAsync(rows));
    }

    private static JobProjectionSource Source(int i) => new()
    {
        Id = Guid.Parse($"0192e8b7-0000-7000-8000-{i:D12}"),
        Title = $"Engineer {i}",
        Description = "desc",
        Status = "Live",
        CompanyName = "Acme",
        CompanyDomain = "acme.com",
        RemotePolicy = "Remote",
        EmploymentType = "FullTime",
        FirstSeenAt = Now,
    };

    private static async IAsyncEnumerable<JobProjectionSource> ToAsync(IEnumerable<JobProjectionSource> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task Counts_that_agree_do_not_re_index()
    {
        GivenCounts(liveJobs: 1000, indexedDocuments: 1000);

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.Value.Drifted.ShouldBeFalse();
        result.Value.Drift.ShouldBe(0d);
        await _index.DidNotReceive().UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drift_within_the_threshold_is_tolerated_without_re_indexing()
    {
        // 5 of 1000 = 0.5%, below the 1% threshold.
        GivenCounts(liveJobs: 1000, indexedDocuments: 995);

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.Value.Drifted.ShouldBeFalse();
        result.Value.Drift.ShouldBe(0.005d, 1e-9);
        await _index.DidNotReceive().UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drift_above_the_threshold_re_indexes_the_live_set()
    {
        // 50 of 1000 = 5%, above the 1% threshold.
        GivenCounts(liveJobs: 1000, indexedDocuments: 950);
        GivenLiveJobs(1000);

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.Value.Drifted.ShouldBeTrue();
        result.Value.Drift.ShouldBe(0.05d, 1e-9);
        result.Value.Reindexed.ShouldBe(1000);
        await _index.Received().UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drift_when_documents_exceed_live_jobs_is_measured_by_absolute_divergence()
    {
        // The index has more documents than PostgreSQL has live jobs (stale documents): |1000-1100|/1000 = 10%.
        GivenCounts(liveJobs: 1000, indexedDocuments: 1100);
        GivenLiveJobs(1000);

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.Value.Drifted.ShouldBeTrue();
        result.Value.Drift.ShouldBe(0.1d, 1e-9);
    }

    [Fact]
    public async Task An_empty_corpus_with_an_empty_index_is_not_drift()
    {
        GivenCounts(liveJobs: 0, indexedDocuments: 0);

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.Value.Drifted.ShouldBeFalse();
        result.Value.Drift.ShouldBe(0d);
    }

    [Fact]
    public async Task An_empty_corpus_with_a_populated_index_is_full_drift_and_re_indexes()
    {
        GivenCounts(liveJobs: 0, indexedDocuments: 42);
        GivenLiveJobs(0);

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.Value.Drifted.ShouldBeTrue();
        result.Value.Drift.ShouldBe(1d);
    }

    [Fact]
    public async Task Reconcile_emits_the_drift_metric()
    {
        GivenCounts(liveJobs: 1000, indexedDocuments: 950);
        GivenLiveJobs(1000);

        double? recorded = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == Telemetry.IndexDrift.Name)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, measurement, _, _) => recorded = measurement);
        listener.Start();

        await CreateService().ReconcileAsync(CancellationToken.None);

        recorded.ShouldNotBeNull();
        recorded!.Value.ShouldBe(0.05d, 1e-9);
    }

    [Fact]
    public async Task A_failed_index_count_fails_the_reconcile()
    {
        _counter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(1000L);
        _index.CountAsync(Arg.Any<CancellationToken>())
            .Returns(Result<long>.Failure(new Error("search.index.unavailable", "down")));

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await _index.DidNotReceive().UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_final_partial_re_index_batch_fails_the_reconcile()
    {
        // Batch size 200, 10 rows -> the final partial batch flush fails.
        GivenCounts(liveJobs: 1000, indexedDocuments: 950);
        GivenLiveJobs(10);
        _index.UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(new Error("search.index.unavailable", "down")));

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failed_full_re_index_batch_fails_the_reconcile()
    {
        // Threshold forces re-index; batch size 200 with 250 rows -> the first full batch flush fails inside the loop.
        GivenCounts(liveJobs: 1000, indexedDocuments: 900);
        GivenLiveJobs(250);
        _index.UpsertManyAsync(Arg.Any<IReadOnlyList<JobDocument>>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(new Error("search.index.unavailable", "down")));

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_re_index_spanning_multiple_full_batches_writes_every_document()
    {
        // 450 rows at batch size 200 -> two full batches then a final partial of 50.
        GivenCounts(liveJobs: 1000, indexedDocuments: 900);
        GivenLiveJobs(450);

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.Value.Reindexed.ShouldBe(450);
    }

    [Fact]
    public async Task A_reconcile_during_an_active_rebuild_skips()
    {
        GivenCounts(liveJobs: 1000, indexedDocuments: 950);
        using var _ = _gate.TryAcquire();

        var result = await CreateService().ReconcileAsync(CancellationToken.None);

        result.Value.Skipped.ShouldBeTrue();
        await _counter.DidNotReceive().CountLiveAsync(Arg.Any<CancellationToken>());
        await _index.DidNotReceive().CountAsync(Arg.Any<CancellationToken>());
    }
}
