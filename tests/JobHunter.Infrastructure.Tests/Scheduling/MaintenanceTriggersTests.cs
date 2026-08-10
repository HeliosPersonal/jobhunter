using JobHunter.Application.Normalization;
using JobHunter.Application.Reprocessing;
using JobHunter.Application.Search;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Search;
using JobHunter.Infrastructure.Scheduling;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Scheduling;

/// <summary>
/// The Hangfire job bodies that wrap a self-contained Application maintenance service rather than publish a
/// pipeline message (SAD §6.3): reprocess, index rebuild and index reconcile. The service logic is unit-tested
/// directly in the Application suite; what these bodies own is the schedule seam — resolve the service, run it,
/// and turn each outcome the service returns as a <see cref="Result{T}"/> (failure / skipped / success) into a
/// log line rather than a throw that would fault the Hangfire server (QG-3). Each trigger is driven through the
/// real service with faked ports so every log branch is exercised end to end.
/// </summary>
public sealed class MaintenanceTriggersTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 4, 0, 0, TimeSpan.Zero);

    // --- ReprocessTrigger: one branch — run the service and log the tally. ---

    [Fact]
    public async Task ReprocessTrigger_runs_the_service_over_the_window_and_completes()
    {
        var reprocessable = Substitute.For<IReprocessableJobsQuery>();
        reprocessable.StreamAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(EmptyStream());
        var service = new ReprocessingService(
            reprocessable,
            Substitute.For<IRawPostingReader>(),
            Substitute.For<IJobSourceRepository>(),
            Substitute.For<ICompanyRepository>(),
            new PostingNormalizerCatalog(Array.Empty<IPostingNormalizer>()),
            new TechnologyTagger(new TechnologyVocabulary([])),
            Substitute.For<IJobRepository>(),
            new SequentialIdGenerator(),
            new FakeClock(Now),
            NullLogger<ReprocessingService>.Instance);
        var trigger = new ReprocessTrigger(service, NullLogger<ReprocessTrigger>.Instance);

        await trigger.RunAsync(Now.AddDays(-7));

        // The window's lower bound is passed straight through to the service query.
        reprocessable.Received(1).StreamAsync(Now.AddDays(-7), Arg.Any<CancellationToken>());
    }

    // --- IndexRebuildTrigger: three branches — failure, skipped, success. ---

    [Fact]
    public async Task IndexRebuildTrigger_logs_and_returns_on_a_failed_rebuild()
    {
        var index = Substitute.For<ISearchIndex>();
        index.DropAndRecreateAsync(Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("search.index.unavailable", "down")));
        var trigger = new IndexRebuildTrigger(RebuildService(index, out _), NullLogger<IndexRebuildTrigger>.Instance);

        await trigger.RunAsync();

        await index.Received(1).DropAndRecreateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexRebuildTrigger_logs_and_returns_when_the_rebuild_skips_on_the_gate()
    {
        var index = Substitute.For<ISearchIndex>();
        var service = RebuildService(index, out var gate);
        using var _ = gate.TryAcquire(); // gate held → the rebuild cannot take it and skips
        var trigger = new IndexRebuildTrigger(service, NullLogger<IndexRebuildTrigger>.Instance);

        await trigger.RunAsync();

        await index.DidNotReceive().DropAndRecreateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexRebuildTrigger_logs_the_tally_on_a_successful_rebuild()
    {
        var index = Substitute.For<ISearchIndex>();
        index.DropAndRecreateAsync(Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        var source = Substitute.For<IJobProjectionSource>();
        source.ProjectLiveAsync(Arg.Any<CancellationToken>()).Returns(EmptyProjections());
        var trigger = new IndexRebuildTrigger(
            RebuildService(index, out _, source), NullLogger<IndexRebuildTrigger>.Instance);

        await trigger.RunAsync();

        await index.Received(1).DropAndRecreateAsync(Arg.Any<CancellationToken>());
    }

    // --- IndexReconcileTrigger: three branches — failure, skipped, success. ---

    [Fact]
    public async Task IndexReconcileTrigger_logs_and_returns_on_a_failed_reconcile()
    {
        var index = Substitute.For<ISearchIndex>();
        var counter = Substitute.For<ILiveJobCounter>();
        counter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(10L);
        index.CountAsync(Arg.Any<CancellationToken>())
            .Returns(Result<long>.Failure(new Error("search.index.unavailable", "down")));
        var trigger = new IndexReconcileTrigger(
            ReconcileService(index, counter, out _), NullLogger<IndexReconcileTrigger>.Instance);

        await trigger.RunAsync();

        await index.Received(1).CountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexReconcileTrigger_logs_and_returns_when_the_reconcile_skips_on_the_gate()
    {
        var index = Substitute.For<ISearchIndex>();
        var counter = Substitute.For<ILiveJobCounter>();
        var service = ReconcileService(index, counter, out var gate);
        using var _ = gate.TryAcquire(); // a rebuild holds the gate → reconcile skips
        var trigger = new IndexReconcileTrigger(service, NullLogger<IndexReconcileTrigger>.Instance);

        await trigger.RunAsync();

        await index.DidNotReceive().CountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndexReconcileTrigger_logs_the_result_on_a_successful_reconcile()
    {
        var index = Substitute.For<ISearchIndex>();
        var counter = Substitute.For<ILiveJobCounter>();
        counter.CountLiveAsync(Arg.Any<CancellationToken>()).Returns(10L);
        // Counts match → drift below threshold → no re-index, a clean success.
        index.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<long>.Success(10L));
        var trigger = new IndexReconcileTrigger(
            ReconcileService(index, counter, out _), NullLogger<IndexReconcileTrigger>.Instance);

        await trigger.RunAsync();

        await index.Received(1).CountAsync(Arg.Any<CancellationToken>());
    }

    private static IndexRebuildService RebuildService(
        ISearchIndex index, out IndexMaintenanceGate gate, IJobProjectionSource? source = null)
    {
        gate = new IndexMaintenanceGate();
        var options = Options.Create(new ReconcileOptions { BatchSize = 200 });
        return new IndexRebuildService(
            source ?? Substitute.For<IJobProjectionSource>(), index, gate, new FakeClock(Now), options,
            NullLogger<IndexRebuildService>.Instance);
    }

    private static IndexReconcileService ReconcileService(
        ISearchIndex index, ILiveJobCounter counter, out IndexMaintenanceGate gate)
    {
        gate = new IndexMaintenanceGate();
        var options = Options.Create(new ReconcileOptions { DriftThreshold = 0.01, BatchSize = 200 });
        return new IndexReconcileService(
            counter, Substitute.For<IJobProjectionSource>(), index, gate, options,
            NullLogger<IndexReconcileService>.Instance);
    }

    private static async IAsyncEnumerable<ReprocessableJob> EmptyStream()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<JobProjectionSource> EmptyProjections()
    {
        await Task.CompletedTask;
        yield break;
    }
}
