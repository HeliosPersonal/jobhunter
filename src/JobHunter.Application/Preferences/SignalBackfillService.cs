using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Preferences;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Preferences;

/// <summary>
/// Backfills outcome signals for application outcomes recorded before F6 began staging them (F7 T03,
/// done-when 5). It is the one-off complement to the live path: <see cref="OutcomeSignalPublisher"/> stages a
/// signal alongside every new transition, but outcomes that predate that path left only their append-only
/// <c>application_transitions</c> row, so this service replays those rows through the same
/// <see cref="Signal.Capture"/> the live path uses. A card tap left no durable trace of its own, so only
/// outcomes can be reconstructed — a fact the report makes plain rather than pretending otherwise.
///
/// <para><strong>Idempotence is structural, twice over.</strong> The query returns only outcomes without a
/// matching signal, and <see cref="ISignalRepository.TryCaptureAsync"/> re-checks the unique
/// <c>(job_id, kind, occurred_at)</c> at insert, so a second run — or two runs racing — captures nothing
/// more (done-when 5). Facts are snapshotted from the job as it is now (the history holds no facts of its
/// own); a job that has since closed has no facts to snapshot, so it is counted and skipped rather than
/// turned into a factless signal that would teach the fitter nothing.</para>
/// </summary>
public sealed class SignalBackfillService(
    IBackfillableOutcomeQuery outcomes,
    IJobFactsSnapshotQuery facts,
    ISignalRepository signals,
    IIdGenerator ids,
    SignalWeights weights,
    ILogger<SignalBackfillService> logger)
{
    private readonly IBackfillableOutcomeQuery _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
    private readonly IJobFactsSnapshotQuery _facts = facts ?? throw new ArgumentNullException(nameof(facts));
    private readonly ISignalRepository _signals = signals ?? throw new ArgumentNullException(nameof(signals));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly SignalWeights _weights = weights ?? throw new ArgumentNullException(nameof(weights));
    private readonly ILogger<SignalBackfillService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Replays every backfillable outcome that occurred at or after <paramref name="occurredFrom"/> and
    /// returns the tally. Never throws for a closed job or an already-captured outcome — both are values it
    /// counts, not exceptions.
    /// </summary>
    public async Task<SignalBackfillReport> BackfillAsync(
        DateTimeOffset occurredFrom,
        CancellationToken cancellationToken)
    {
        var examined = 0;
        var captured = 0;
        var skipped = 0;
        var withoutFacts = 0;

        await foreach (var outcome in _outcomes.StreamAsync(occurredFrom, cancellationToken).ConfigureAwait(false))
        {
            examined++;

            var snapshot = await _facts.SnapshotAsync(outcome.JobId, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                // The job has since closed or was superseded — the history holds no facts of its own, and a
                // signal needs non-empty facts, so there is nothing to snapshot. Count it and move on rather
                // than fabricate a factless signal that teaches the fitter nothing.
                withoutFacts++;
                _logger.LogInformation(
                    "Backfill outcome {Kind} for job {JobId} has no live facts to snapshot; skipping.",
                    outcome.ToStatus, outcome.JobId);
                continue;
            }

            var signal = Signal.Capture(
                _ids.NewId(),
                outcome.JobId,
                outcome.ApplicationId,
                KindOf(outcome.ToStatus),
                snapshot,
                outcome.OccurredAt,
                _weights);

            // The unique (job_id, kind, occurred_at) arbitrates: a false means the signal was already present
            // (a re-run, or the live path already staged it), so the backfill is idempotent (done-when 5).
            if (await _signals.TryCaptureAsync(signal, cancellationToken).ConfigureAwait(false))
            {
                captured++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation(
            "Signal backfill from {OccurredFrom:o}: {Examined} examined, {Captured} captured, {Skipped} " +
            "already present, {WithoutFacts} without live facts.",
            occurredFrom, examined, captured, skipped, withoutFacts);

        return new SignalBackfillReport(examined, captured, skipped, withoutFacts);
    }

    private static SignalKind KindOf(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Applied => SignalKind.Applied,
        ApplicationStatus.Interview => SignalKind.Interview,
        ApplicationStatus.Offer => SignalKind.Offer,
        ApplicationStatus.Rejected => SignalKind.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a backfillable outcome status."),
    };
}
