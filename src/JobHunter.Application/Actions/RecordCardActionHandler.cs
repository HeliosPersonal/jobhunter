using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Actions;

/// <summary>
/// Turns a card tap into durable evidence (F5 T10, AC-03/AC-08). It reads the job's facts at the moment of
/// the tap and captures one card-action <see cref="Signal"/> — the snapshot and the capture in a single
/// step, so evidence is never a separate action that can fail on its own. The snapshot is read here, not
/// joined at fitting time, so a later job edit cannot rewrite what the Owner reacted to (F7-T03 asserts
/// this directly).
///
/// <para>Not every action is an F5 signal. <see cref="CardAction.Open"/> is a URL button that never sends a
/// callback, and <see cref="CardAction.Applied"/> is an F6 <em>outcome</em> kind that requires an
/// application id F5 does not have — its durable record is the <c>OwnerActionRecorded</c> event F6 consumes,
/// not a card-action signal minted here. Both return <see cref="CardActionOutcome.RecordedElsewhere"/> so
/// the Telegram layer still acknowledges and updates the keyboard without this handler inventing evidence it
/// does not own.</para>
///
/// <para>Idempotence lives at the database: <see cref="ISignalRepository.TryCaptureAsync"/> returns
/// <c>false</c> when an identical <c>(job_id, kind, occurred_at)</c> signal already exists, so a double-tap
/// captures nothing more and the Owner is still re-acknowledged.</para>
/// </summary>
public sealed class RecordCardActionHandler
{
    private readonly IJobFactsSnapshotQuery _facts;
    private readonly ISignalRepository _signals;
    private readonly IIdGenerator _ids;

    public RecordCardActionHandler(IJobFactsSnapshotQuery facts, ISignalRepository signals, IIdGenerator ids)
    {
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<CardActionOutcome> Handle(RecordCardActionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Open opens the posting directly (URL button, no callback) and Applied is an F6 outcome recorded
        // through OwnerActionRecorded — neither is a card-action signal F5 mints. Do not even read the
        // snapshot for them: there is no F5 evidence to attach it to.
        if (command.Action is CardAction.Open or CardAction.Applied)
        {
            return CardActionOutcome.RecordedElsewhere;
        }

        var snapshot = await _facts.SnapshotAsync(command.JobId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            // The job is gone or closed — record nothing invalid; the Owner is told plainly (AC-09).
            return CardActionOutcome.JobUnavailable;
        }

        var signal = Signal.Capture(
            _ids.NewId(),
            command.JobId,
            applicationId: null,
            KindOf(command.Action),
            snapshot,
            command.OccurredAt,
            SignalWeights.Default);

        var captured = await _signals.TryCaptureAsync(signal, cancellationToken).ConfigureAwait(false);
        return captured ? CardActionOutcome.Captured : CardActionOutcome.AlreadyCaptured;
    }

    private static SignalKind KindOf(CardAction action) => action switch
    {
        CardAction.Ignore => SignalKind.Ignored,
        CardAction.Save => SignalKind.Saved,
        CardAction.Rate => SignalKind.Rated,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not a capturable card action."),
    };
}
