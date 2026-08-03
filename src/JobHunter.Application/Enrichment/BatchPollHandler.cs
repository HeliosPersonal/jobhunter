using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Enrichment;

/// <summary>
/// The batch poller (F3 SAD §6.2/§6.3, T11). It consumes <see cref="BatchPollDue"/>, asks the provider
/// for the batch's status, and <strong>re-enqueues itself</strong> on the backoff schedule rather than
/// looping — which is what makes the backoff survive a restart (S5). The provider batch is reached
/// through its persisted id, so a redelivered poll polls the same batch and never resubmits — the poller
/// has no submission path at all (AC-05, QG-1).
///
/// <para>Two independent give-up thresholds bound the polling. The daily <em>delivery deadline</em>
/// (06:45 Europe/Kyiv) ships whatever completed and carries the rest to the next Run so the 07:00 digest
/// is never delayed (AC-09); the absolute <em>6 h cap</em> is the hard safety net for a batch stuck far
/// from the deadline. In both cases the batch is failed, its items are marked carried-over so the next
/// Run re-scopes them, and the Run advances to <see cref="RunState.Matching"/> with a recorded carry-over
/// count. When the provider reports the batch ended, the poller hands off to result processing (T12) via
/// <see cref="BatchResultsReady"/> and does not advance the Run itself.</para>
/// </summary>
public sealed class BatchPollHandler(
    IRunRepository runs,
    ILlmBatchClient client,
    IJitter jitter,
    IClock clock,
    PollOptions options,
    ILogger<BatchPollHandler> logger)
{
    private const BatchStage Stage = BatchStage.Enrichment;
    private const ModelTier Tier = ModelTier.Cheap;

    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly ILlmBatchClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IJitter _jitter = jitter ?? throw new ArgumentNullException(nameof(jitter));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly PollOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<BatchPollHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(BatchPollDue message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("BatchPollDue for unknown Run {RunId}; ignoring.", message.RunId);
            return;
        }

        if (RunTransitions.IsTerminal(run.State))
        {
            _logger.LogInformation("Run {RunId} is terminal ({State}); nothing to poll.", run.Id, run.State);
            return;
        }

        var batch = await _runs.FindBatchAsync(run.Id, Stage, Tier, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            _logger.LogWarning(
                "Run {RunId} is {State} but has no enrichment batch to poll; ignoring.", run.Id, run.State);
            return;
        }

        if (batch.State is BatchState.Completed or BatchState.Failed or BatchState.Expired)
        {
            _logger.LogInformation(
                "Batch {ProviderBatchId} is already {State}; no further polling.", batch.ProviderBatchId, batch.State);
            return;
        }

        var status = await _client.GetStatusAsync(batch.ProviderBatchId, cancellationToken).ConfigureAwait(false);
        batch.RecordPoll();

        var now = _clock.UtcNow;
        switch (status.State)
        {
            case ProviderBatchState.Ended:
                await HandleEndedAsync(run, batch, now, bus, cancellationToken).ConfigureAwait(false);
                return;

            case ProviderBatchState.Expired:
            case ProviderBatchState.Cancelled:
                await CarryOverAsync(
                    run, batch, status.State == ProviderBatchState.Expired ? BatchState.Expired : BatchState.Failed,
                    now, bus, cancellationToken).ConfigureAwait(false);
                return;

            case ProviderBatchState.InProgress:
            default:
                await HandleInProgressAsync(run, batch, now, bus, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task HandleEndedAsync(
        Run run, Batch batch, DateTimeOffset now, IMessageBus bus, CancellationToken cancellationToken)
    {
        // The provider is done. The poller does not parse or advance the Run — that is T12's job, keyed on
        // the same Run so reprocessing converges on the unique (job_id, run_id) enrichment (AC-06).
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await bus.PublishAsync(new BatchResultsReady(run.Id, batch.Id, batch.ProviderBatchId)).ConfigureAwait(false);

        _logger.LogInformation(
            "Batch {ProviderBatchId} for Run {RunId} ended after {Attempts} poll(s); handing off to result processing.",
            batch.ProviderBatchId, run.Id, batch.PollAttempts);
    }

    private async Task HandleInProgressAsync(
        Run run, Batch batch, DateTimeOffset now, IMessageBus bus, CancellationToken cancellationToken)
    {
        // Move to InProgress on the first poll that observes it (idempotent: a second InProgress poll is a
        // no-op transition returning a failure we ignore, because the batch is already InProgress).
        batch.TransitionTo(BatchState.InProgress, now);

        if (IsPastDeadline(batch, now) || IsPastCap(batch, now))
        {
            await CarryOverAsync(run, batch, BatchState.Failed, now, bus, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Re-enqueue self on the backoff schedule, jittered so several batches do not poll in lockstep
        // (S5, SAD §8). The delay is a value asserted against FakeClock — the poller never sleeps.
        var delay = _jitter.Apply(PollBackoff.DelayForAttempt(batch.PollAttempts));
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await bus.ScheduleAsync(new BatchPollDue(run.Id), delay).ConfigureAwait(false);

        _logger.LogInformation(
            "Batch {ProviderBatchId} still in progress (poll {Attempts}); next poll in {Delay}.",
            batch.ProviderBatchId, batch.PollAttempts, delay);
    }

    private async Task CarryOverAsync(
        Run run, Batch batch, BatchState terminal, DateTimeOffset now, IMessageBus bus, CancellationToken cancellationToken)
    {
        // The batch did not finish in time (deadline, cap, or provider expiry). Ship what exists and carry
        // its items to the next Run so a slow night is a deferral, not a loss (AC-09). 07:00 is never delayed.
        var items = await _runs.FindBatchItemsAsync(batch.Id, cancellationToken).ConfigureAwait(false);
        var carriedOver = 0;
        foreach (var item in items)
        {
            if (item.State is BatchItemState.Pending)
            {
                // Mark unfinished items as a provider error so FindRetriableJobIdsAsync re-scopes them next
                // Run at the cheap tier (AC-08). Already-parsed items keep their enrichment.
                item.MarkProviderError("Batch did not complete before the delivery deadline.", rawResult: null);
                carriedOver++;
            }
        }

        batch.TransitionTo(terminal, now);
        run.RecordCarryOver(carriedOver);
        AdvanceToMatching(run, now);
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var enrichedCount = items.Count - carriedOver;
        await bus.PublishAsync(new EnrichmentCompleted(run.Id, enrichedCount, carriedOver, now)).ConfigureAwait(false);

        _logger.LogWarning(
            "Batch {ProviderBatchId} for Run {RunId} did not complete in time; marked {State}, {Carried} item(s) carried over.",
            batch.ProviderBatchId, run.Id, terminal, carriedOver);
    }

    private static void AdvanceToMatching(Run run, DateTimeOffset now)
    {
        // From Enriching the only forward edge for a partial ship is Matching (RunTransitions); a resume
        // that already advanced is a terminal/other state and returns a failure we intentionally ignore.
        run.TransitionTo(RunState.Matching, now);
    }

    private bool IsPastDeadline(Batch batch, DateTimeOffset now)
    {
        if (_options.DeliveryDeadlineLocalTime is not { } localTime)
        {
            return false;
        }

        var deadline = PollDeadline.NextDeadlineAfter(batch.SubmittedAt, localTime, _options.TimeZone);
        return now >= deadline;
    }

    private bool IsPastCap(Batch batch, DateTimeOffset now) =>
        now - batch.SubmittedAt >= _options.MaxPollDuration;
}
