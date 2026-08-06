using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Preferences;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// F7 T03 (done-when 5): the backfill replays historical application outcomes into signals and is idempotent.
/// It reads only the outcomes the query surfaces (those without a matching signal), snapshots the job's facts
/// as they are now — the history holds none — and captures one weighted signal per outcome through the same
/// idempotent <see cref="ISignalRepository.TryCaptureAsync"/> the live path uses. A closed job has no facts to
/// snapshot, so it is counted and skipped rather than turned into a factless signal; an outcome the repository
/// reports as already present is skipped, so a second run captures nothing more. Everything is stubbed: the
/// service holds no notifier and no network port, so a backfill can only write signals — it never acts for the
/// Owner.
/// </summary>
public sealed class SignalBackfillServiceTests
{
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredAt = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    private readonly IBackfillableOutcomeQuery _outcomes = Substitute.For<IBackfillableOutcomeQuery>();
    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly ISignalRepository _signals = Substitute.For<ISignalRepository>();
    private readonly IIdGenerator _ids = new SequentialIdGenerator();
    private readonly SignalWeights _weights = SignalWeights.Default;

    private SignalBackfillService CreateService() =>
        new(_outcomes, _facts, _signals, _ids, _weights, NullLogger<SignalBackfillService>.Instance);

    private static JobFacts Facts() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
    {
        [Dimension.RemotePolicy] = [nameof(RemotePolicy.Remote)],
        [Dimension.EmploymentType] = [nameof(EmploymentType.FullTime)],
    });

    private void GivenOutcomes(params BackfillableOutcome[] outcomes) =>
        _outcomes.StreamAsync(From, Arg.Any<CancellationToken>()).Returns(Stream(outcomes));

    private static async IAsyncEnumerable<BackfillableOutcome> Stream(BackfillableOutcome[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            yield return outcome;
        }

        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(ApplicationStatus.Applied, SignalKind.Applied)]
    [InlineData(ApplicationStatus.Interview, SignalKind.Interview)]
    [InlineData(ApplicationStatus.Offer, SignalKind.Offer)]
    [InlineData(ApplicationStatus.Rejected, SignalKind.Rejected)]
    public async Task Each_historical_outcome_captures_one_weighted_signal_of_its_kind(
        ApplicationStatus outcome, SignalKind kind)
    {
        var jobId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        GivenOutcomes(new BackfillableOutcome(jobId, applicationId, outcome, OccurredAt));
        _facts.SnapshotAsync(jobId, Arg.Any<CancellationToken>()).Returns(Facts());
        _signals.TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>()).Returns(true);

        var report = await CreateService().BackfillAsync(From, CancellationToken.None);

        report.Examined.ShouldBe(1);
        report.Captured.ShouldBe(1);
        report.Skipped.ShouldBe(0);
        report.WithoutFacts.ShouldBe(0);

        await _signals.Received(1).TryCaptureAsync(
            Arg.Is<Signal>(s =>
                s != null
                && s.JobId == jobId
                && s.ApplicationId == applicationId
                && s.Kind == kind
                && s.Weight == SignalWeights.Default.WeightFor(kind)
                && s.OccurredAt == OccurredAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_outcome_already_captured_is_skipped_so_a_second_run_writes_nothing()
    {
        var jobId = Guid.CreateVersion7();
        GivenOutcomes(new BackfillableOutcome(jobId, Guid.CreateVersion7(), ApplicationStatus.Applied, OccurredAt));
        _facts.SnapshotAsync(jobId, Arg.Any<CancellationToken>()).Returns(Facts());
        // The unique constraint arbitrates: TryCaptureAsync reports the row already existed.
        _signals.TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>()).Returns(false);

        var report = await CreateService().BackfillAsync(From, CancellationToken.None);

        report.Examined.ShouldBe(1);
        report.Captured.ShouldBe(0);
        report.Skipped.ShouldBe(1);
    }

    [Fact]
    public async Task A_closed_job_has_no_facts_to_snapshot_so_its_outcome_is_counted_and_skipped()
    {
        var jobId = Guid.CreateVersion7();
        GivenOutcomes(new BackfillableOutcome(jobId, Guid.CreateVersion7(), ApplicationStatus.Rejected, OccurredAt));
        _facts.SnapshotAsync(jobId, Arg.Any<CancellationToken>()).Returns((JobFacts?)null);

        var report = await CreateService().BackfillAsync(From, CancellationToken.None);

        report.Examined.ShouldBe(1);
        report.Captured.ShouldBe(0);
        report.WithoutFacts.ShouldBe(1);
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_history_is_a_no_op()
    {
        GivenOutcomes();

        var report = await CreateService().BackfillAsync(From, CancellationToken.None);

        report.ShouldBe(new SignalBackfillReport(0, 0, 0, 0));
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
    }
}
