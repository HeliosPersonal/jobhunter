using JobHunter.Application.Enrichment;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Matching;

/// <summary>
/// The matching batch poller (F4 SAD §6.1, T06). The deep-tier twin of F3's <see cref="BatchPollHandler"/>:
/// it consumes <see cref="MatchingPollDue"/>, asks the provider for the batch's status, and
/// <strong>re-enqueues itself</strong> on the shared backoff schedule rather than looping, so the backoff
/// survives a restart (S5). It reuses F3's poll <em>mechanism</em> unchanged — <see cref="PollOptions"/>,
/// <see cref="PollBackoff"/>, <see cref="PollDeadline"/> and <see cref="IJitter"/> — differing only in the
/// stage (<see cref="BatchStage.Matching"/>), the tier (<see cref="ModelTier.Deep"/>), and the forward
/// edge it advances the Run along on a partial ship (<see cref="RunState.Ranking"/>). F3's poll handler is
/// hard-wired to the enrichment stage, so matching carries its own poller rather than modifying an F3 file
/// (T05 done-when: no F3 file is modified).
///
/// <para>The provider batch is reached through its persisted id, so a redelivered poll polls the same batch
/// and never resubmits — the poller has no submission path at all (AC-05, QG-1). When the provider reports
/// the batch ended, the poller hands off to result processing (T06) via <see cref="MatchingResultsReady"/>
/// and does not advance the Run itself. On the delivery deadline or the 6 h cap it ships whatever completed
/// and carries the rest to the next Run so the 07:00 digest is never delayed (AC-09), advancing the Run to
/// <c>Ranking</c> and publishing a <see cref="MatchingCompleted"/> so a reduced digest still flows.</para>
/// </summary>
public sealed class MatchingPollHandler(
    IRunRepository runs,
    ILlmBatchClient client,
    IJitter jitter,
    IClock clock,
    PollOptions options,
    ILogger<MatchingPollHandler> logger)
{
    private const BatchStage Stage = BatchStage.Matching;
    private const ModelTier Tier = ModelTier.Deep;

    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly ILlmBatchClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IJitter _jitter = jitter ?? throw new ArgumentNullException(nameof(jitter));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly PollOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<MatchingPollHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(MatchingPollDue message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("MatchingPollDue for unknown Run {RunId}; ignoring.", message.RunId);
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
                "Run {RunId} is {State} but has no matching batch to poll; ignoring.", run.Id, run.State);
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
                await HandleEndedAsync(run, batch, bus, cancellationToken).ConfigureAwait(false);
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
        Run run, Batch batch, IMessageBus bus, CancellationToken cancellationToken)
    {
        // The provider is done. The poller does not parse or advance the Run — that is result processing's
        // job, keyed on the same Run so reprocessing converges on the unique (job_id, run_id, profile_id)
        // match (invariant 3).
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await bus.PublishAsync(new MatchingResultsReady(run.Id, batch.Id, batch.ProviderBatchId)).ConfigureAwait(false);

        _logger.LogInformation(
            "Matching batch {ProviderBatchId} for Run {RunId} ended after {Attempts} poll(s); handing off to result processing.",
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

        // Re-enqueue self on the shared backoff schedule, jittered so several batches do not poll in
        // lockstep (S5, SAD §8). The delay is a value asserted against FakeClock — the poller never sleeps.
        var delay = _jitter.Apply(PollBackoff.DelayForAttempt(batch.PollAttempts));
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await bus.ScheduleAsync(new MatchingPollDue(run.Id), delay).ConfigureAwait(false);

        _logger.LogInformation(
            "Matching batch {ProviderBatchId} still in progress (poll {Attempts}); next poll in {Delay}.",
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
                // Run (AC-08). Already-parsed items keep their match.
                item.MarkProviderError("Matching batch did not complete before the delivery deadline.", rawResult: null);
                carriedOver++;
            }
        }

        batch.TransitionTo(terminal, now);
        run.RecordCarryOver(carriedOver);
        AdvanceToRanking(run, now);
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var matchedCount = items.Count - carriedOver;
        await bus.PublishAsync(new MatchingCompleted(run.Id, matchedCount, carriedOver, CostUsd: 0m, now))
            .ConfigureAwait(false);

        _logger.LogWarning(
            "Matching batch {ProviderBatchId} for Run {RunId} did not complete in time; marked {State}, {Carried} item(s) carried over.",
            batch.ProviderBatchId, run.Id, terminal, carriedOver);
    }

    private static void AdvanceToRanking(Run run, DateTimeOffset now)
    {
        // From Matching the only forward edge for a partial ship is Ranking (RunTransitions); a resume that
        // already advanced is a terminal/other state and returns a failure we intentionally ignore.
        run.TransitionTo(RunState.Ranking, now);
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
