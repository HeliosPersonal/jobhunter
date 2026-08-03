using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Enrichment;

/// <summary>
/// The one spend-committing step of the Run (F3 SAD §6.2, T10, ADR-F3-0002). It builds the enrichment
/// batch from the Run's scope, prices it, and enforces the cost ceiling as a <em>precondition</em>: the
/// estimate is written to the ledger <strong>before</strong> the client is called (AC-04), the ceiling is
/// checked against estimates-plus-actuals, and only if it holds is <see cref="ILlmBatchClient.SubmitAsync"/>
/// invoked at all. A breach never reaches the client — the Run becomes <see cref="RunState.CostAborted"/>
/// and <see cref="RunCostAborted"/> is published so a reduced digest still ships (invariant 6, QG-2, AC-03).
///
/// <para>The ordering is the entire content of ADR-F3-0002. The estimate ledger entry commits in its own
/// transaction with no batch to point at yet (its <c>batch_id</c> is null); the batch row — carrying the
/// provider id the moment the provider accepts the submission — commits in a second transaction together
/// with the items and the Run's move to <see cref="RunState.Enriching"/> (SAD §6.2 steps 5–6, S2). A crash
/// between the two leaves an over-counted estimate, which is the safe direction.</para>
///
/// <para>Idempotency (QG-1): a redelivered <see cref="EnrichmentSubmissionDue"/> after the batch already
/// committed does not resubmit — the batch is found by its <c>(run, stage, tier)</c> key and the handler
/// polls instead; a redelivery after only the estimate committed does not re-estimate — the existing
/// estimate is reused. The unique <c>(run_id, stage, tier)</c> index is the hard arbiter behind both.</para>
/// </summary>
public sealed class EnrichmentSubmitHandler(
    IRunRepository runs,
    IEnrichmentScopeQuery scope,
    IEnrichmentRequestBuilder requestBuilder,
    ICostAccountant accountant,
    ILlmBatchClient client,
    IClock clock,
    IIdGenerator ids,
    ILogger<EnrichmentSubmitHandler> logger)
{
    private const BatchStage Stage = BatchStage.Enrichment;
    private const ModelTier Tier = ModelTier.Cheap;

    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IEnrichmentScopeQuery _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly IEnrichmentRequestBuilder _requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
    private readonly ICostAccountant _accountant = accountant ?? throw new ArgumentNullException(nameof(accountant));
    private readonly ILlmBatchClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly ILogger<EnrichmentSubmitHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(
        EnrichmentSubmissionDue message,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("EnrichmentSubmissionDue for unknown Run {RunId}; ignoring.", message.RunId);
            return;
        }

        if (RunTransitions.IsTerminal(run.State))
        {
            _logger.LogInformation("Run {RunId} is terminal ({State}); nothing to submit.", run.Id, run.State);
            return;
        }

        // Idempotency: a batch already recorded for this (run, stage, tier) means submission already
        // happened — the provider was paid once, and a redelivery must poll, never resubmit (AC-05, QG-1).
        var existingBatch = await _runs.FindBatchAsync(run.Id, Stage, Tier, cancellationToken).ConfigureAwait(false);
        if (existingBatch is not null)
        {
            _logger.LogInformation(
                "Run {RunId} already has enrichment batch {ProviderBatchId}; polling rather than resubmitting.",
                run.Id, existingBatch.ProviderBatchId);
            await bus.PublishAsync(new BatchPollDue(run.Id)).ConfigureAwait(false);
            return;
        }

        if (run.State != RunState.Created)
        {
            // No batch yet but past Created: a downstream stage owns this Run now; submission is not our call.
            _logger.LogInformation(
                "Run {RunId} is in {State} with no enrichment batch; submission is skipped.", run.Id, run.State);
            return;
        }

        var jobs = await LoadScopeAsync(run, cancellationToken).ConfigureAwait(false);
        if (jobs.Count == 0)
        {
            // The scope emptied out between start and submit (e.g. every job closed). Complete without
            // spending — silence is worse than a reduced digest (brief §9). SetScope keeps the count honest.
            await CompleteWithoutSubmittingAsync(run, bus, cancellationToken).ConfigureAwait(false);
            return;
        }

        var request = _requestBuilder.Build(jobs);
        var renderedPrompts = request.Items.Select(i => i.UserContent).ToList();
        var estimate = _accountant.Estimate(Tier, renderedPrompts, request.MaxOutputTokensPerItem);

        // The ceiling is a precondition, not an alarm: it is checked against what this Run has already
        // spent plus this estimate, and the client is not called at all when it would breach (QG-2, AC-03).
        var projected = run.SpentUsd + estimate.CostUsd;
        if (projected > run.CeilingUsd)
        {
            await AbortOnCeilingAsync(run, estimate, projected, bus, cancellationToken).ConfigureAwait(false);
            return;
        }

        // AC-04: the estimate is written to the ledger and committed BEFORE the client is called, so the
        // ceiling is always checked against a ledger that already includes it — the race cannot under-count.
        // A resume that already committed the estimate reuses it rather than double-counting (checkpoint 3).
        var alreadyEstimated = await _runs
            .HasLedgerEntryAsync(run.Id, Stage, Tier, LedgerEntryKind.Estimated, cancellationToken)
            .ConfigureAwait(false);
        if (!alreadyEstimated)
        {
            _runs.AddLedgerEntry(new CostLedgerEntry(
                _ids.NewId(), run.Id, batchId: null, Stage, Tier, LedgerEntryKind.Estimated,
                estimate.CostUsd, estimate.InputTokens, estimate.OutputTokens, _clock.UtcNow));
            run.SetSpend(projected);
            await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // Only now — after the estimate is durable — is the one spend-committing call made, and even that is
        // guarded by reconciliation so the single window where money could be spent without a record is closed.
        var submission = new BatchSubmission(Tier, request.PromptVersion, request.Items);
        var providerBatchId = await ReconcileOrSubmitAsync(run, submission, alreadyEstimated, cancellationToken)
            .ConfigureAwait(false);

        // The provider id is persisted immediately, in the same transaction that records the batch, its
        // items and the Run's move to Enriching (SAD §6.2 steps 5–6, done-when #3). The unique index makes
        // a second batch for this Run impossible, so a crash after submit reconciles rather than repays.
        var now = _clock.UtcNow;
        var batch = new Batch(
            _ids.NewId(), run.Id, Stage, Tier, providerBatchId, request.PromptVersion, request.Items.Count, now);
        _runs.AddBatch(batch);

        foreach (var item in request.Items)
        {
            var jobId = Guid.Parse(item.CustomId);
            _runs.AddBatchItem(new BatchItem(_ids.NewId(), batch.Id, item.CustomId, jobId));
        }

        var transition = run.TransitionTo(RunState.Enriching, now);
        if (transition.IsFailure)
        {
            // Programmer error — the Created-state guard above should make this unreachable.
            throw new InvalidOperationException(
                $"Run {run.Id} could not move to Enriching after submission: {transition.Error.Message}");
        }

        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new EnrichmentBatchSubmitted(
            run.Id, batch.Id, providerBatchId, request.PromptVersion, request.Items.Count, now))
            .ConfigureAwait(false);
        await bus.PublishAsync(new BatchPollDue(run.Id)).ConfigureAwait(false);

        _logger.LogInformation(
            "Submitted enrichment batch {ProviderBatchId} for Run {RunId}: {ItemCount} items, estimate {Estimate}.",
            providerBatchId, run.Id, request.Items.Count, estimate);
    }

    /// <summary>
    /// The mitigation for SAD §11 D5 and crash-matrix checkpoint 4. There is a one-statement window between
    /// <see cref="ILlmBatchClient.SubmitAsync"/> returning a provider id and the batch row committing that
    /// id; a crash inside it leaves the provider holding a batch the database has no record of. A naive
    /// resume would resubmit and pay twice. So when a prior attempt is known to have reached at least the
    /// estimate commit (<paramref name="priorAttempt"/>), the client's recent batches are listed first: if
    /// one created since this Run began already exists, it is <em>adopted</em> rather than resubmitted, and
    /// the client is never called a second time. A single active Run submits exactly one enrichment batch,
    /// so at most one such batch can exist and the adoption is unambiguous. On a first attempt, or when no
    /// orphan is found, the one spend-committing call is made.
    /// </summary>
    private async Task<string> ReconcileOrSubmitAsync(
        Run run, BatchSubmission submission, bool priorAttempt, CancellationToken cancellationToken)
    {
        if (priorAttempt)
        {
            var recent = await _client.ListRecentBatchesAsync(run.StartedAt, cancellationToken).ConfigureAwait(false);
            if (recent.Count > 0)
            {
                var adopted = recent[0];
                _logger.LogWarning(
                    "Run {RunId} found an unrecorded provider batch {ProviderBatchId} on resume; adopting it rather than resubmitting (D5, checkpoint 4).",
                    run.Id, adopted.ProviderBatchId);
                return adopted.ProviderBatchId;
            }
        }

        return await _client.SubmitAsync(submission, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<EnrichmentJobContent>> LoadScopeAsync(Run run, CancellationToken cancellationToken)
    {
        // The window's jobs plus the previous Run's failed items retrying once (AC-08).
        var carriedOver = await _runs.FindRetriableJobIdsAsync(cancellationToken).ConfigureAwait(false);
        return await _scope
            .InScopeAsync(run.CutoffFrom, run.CutoffTo, carriedOver, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CompleteWithoutSubmittingAsync(Run run, IMessageBus bus, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        run.SetScope(0);
        run.TransitionTo(RunState.Enriching, now);
        run.TransitionTo(RunState.Matching, now);
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new EnrichmentCompleted(run.Id, EnrichedCount: 0, FailedCount: 0, now))
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Run {RunId} had an empty enrichment scope at submission; completed without spending.", run.Id);
    }

    private async Task AbortOnCeilingAsync(
        Run run,
        CostEstimate estimate,
        decimal projected,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var reason = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Enrichment estimate ${estimate.CostUsd:0.0000} plus spend ${run.SpentUsd:0.0000} would breach the ${run.CeilingUsd:0.0000} ceiling.");

        run.Abort(reason, now, costBreach: true);
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new RunCostAborted(
            run.Id, estimate.CostUsd, run.CeilingUsd, run.SpentUsd, reason, now)).ConfigureAwait(false);

        _logger.LogWarning(
            "Run {RunId} cost-aborted before submission: projected ${Projected} over ceiling ${Ceiling}. The client was not called.",
            run.Id, projected, run.CeilingUsd);
    }
}
