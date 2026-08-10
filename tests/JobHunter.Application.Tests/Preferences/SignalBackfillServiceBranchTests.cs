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
/// The remaining branch arms of <see cref="SignalBackfillService"/> that the primary suite in
/// <see cref="SignalBackfillServiceTests"/> does not reach: the dependency-guard field initialisers, and the
/// defensive default of the private status-to-kind map. The map is exhaustive for the four backfillable
/// outcomes; any other status is a programmer error — the query is supposed to surface only outcomes — so it
/// is thrown, not counted, which is exactly what this asserts.
/// </summary>
public sealed class SignalBackfillServiceBranchTests
{
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredAt = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    private readonly IBackfillableOutcomeQuery _outcomes = Substitute.For<IBackfillableOutcomeQuery>();
    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly ISignalRepository _signals = Substitute.For<ISignalRepository>();
    private readonly IIdGenerator _ids = new SequentialIdGenerator();
    private readonly SignalWeights _weights = SignalWeights.Default;

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var logger = NullLogger<SignalBackfillService>.Instance;

        Should.Throw<ArgumentNullException>(() => new SignalBackfillService(null!, _facts, _signals, _ids, _weights, logger));
        Should.Throw<ArgumentNullException>(() => new SignalBackfillService(_outcomes, null!, _signals, _ids, _weights, logger));
        Should.Throw<ArgumentNullException>(() => new SignalBackfillService(_outcomes, _facts, null!, _ids, _weights, logger));
        Should.Throw<ArgumentNullException>(() => new SignalBackfillService(_outcomes, _facts, _signals, null!, _weights, logger));
        Should.Throw<ArgumentNullException>(() => new SignalBackfillService(_outcomes, _facts, _signals, _ids, null!, logger));
        Should.Throw<ArgumentNullException>(() => new SignalBackfillService(_outcomes, _facts, _signals, _ids, _weights, null!));
    }

    [Fact]
    public async Task A_non_outcome_status_that_slips_through_is_a_programmer_error_not_a_signal()
    {
        var jobId = Guid.CreateVersion7();
        _outcomes.StreamAsync(From, Arg.Any<CancellationToken>())
            .Returns(Stream(new BackfillableOutcome(jobId, Guid.CreateVersion7(), ApplicationStatus.New, OccurredAt)));
        _facts.SnapshotAsync(jobId, Arg.Any<CancellationToken>()).Returns(Facts());

        var service = new SignalBackfillService(
            _outcomes, _facts, _signals, _ids, _weights, NullLogger<SignalBackfillService>.Instance);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => service.BackfillAsync(From, CancellationToken.None));
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
    }

    private static JobFacts Facts() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
    {
        [Dimension.RemotePolicy] = [nameof(RemotePolicy.Remote)],
        [Dimension.EmploymentType] = [nameof(EmploymentType.FullTime)],
    });

    private static async IAsyncEnumerable<BackfillableOutcome> Stream(params BackfillableOutcome[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            yield return outcome;
        }

        await Task.CompletedTask;
    }
}
