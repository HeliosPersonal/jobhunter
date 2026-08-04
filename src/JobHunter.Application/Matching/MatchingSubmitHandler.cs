using System.Globalization;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Profiles;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Matching;

/// <summary>
/// The second spend-committing step of the Run and the one that puts the CV to work (F4 SAD §6.1, T05). It
/// consumes <see cref="EnrichmentCompleted"/> — which arrives when the Run is already in
/// <see cref="RunState.Matching"/> — builds the deep-tier matching batch from the Run's scope plus each
/// job's enrichment plus the active CV, prices it, and enforces the cost ceiling as a <em>precondition</em>
/// exactly as enrichment does (ADR-F3-0002, invariant 6): the estimate is written to the ledger
/// <strong>before</strong> the client is called, the ceiling is checked against estimates-plus-actuals, and
/// only if it holds is <see cref="ILlmBatchClient.SubmitAsync"/> invoked at all. A breach never reaches the
/// client — the Run becomes <see cref="RunState.CostAborted"/> and <see cref="RunCostAborted"/> is published
/// so a reduced digest still ships (QG-2, invariant 6).
///
/// <para>This handler <strong>adds a stage, not a mechanism</strong> (T05): it reuses F3's
/// <see cref="ILlmBatchClient"/>, <see cref="ICostAccountant"/> and Run machinery unchanged, differing from
/// enrichment only in the stage (<see cref="BatchStage.Matching"/>), the tier
/// (<see cref="ModelTier.Deep"/>), and the fact that the CV crosses the boundary here — folded into the
/// items by <see cref="IMatchRequestBuilder"/>, the one place CV text is materialised. It does <em>not</em>
/// transition the Run on the submit path: the matching poller (T06) advances
/// <see cref="RunState.Matching"/> → <see cref="RunState.Ranking"/> once results are processed. The only
/// transition here is the no-work short-circuit, which completes an empty or CV-less Run straight to
/// <see cref="RunState.Ranking"/> so the pipeline never stalls (brief §9).</para>
///
/// <para>Idempotency (QG-1): a redelivered <see cref="EnrichmentCompleted"/> after the batch already
/// committed does not resubmit — the batch is found by its <c>(run, Matching, Deep)</c> key and the handler
/// publishes a poll instead; a redelivery after only the estimate committed reuses that estimate. The
/// unique <c>(run_id, stage, tier)</c> index is the hard arbiter behind both, so a Run submits at most one
/// matching batch.</para>
/// </summary>
public sealed class MatchingSubmitHandler(
    IRunRepository runs,
    IMatchScopeQuery scope,
    IMatchRequestBuilder requestBuilder,
    IProfileRepository profiles,
    ICvVersionRepository cvVersions,
    ICostAccountant accountant,
    ILlmBatchClient client,
    IClock clock,
    IIdGenerator ids,
    ILogger<MatchingSubmitHandler> logger)
{
    private const BatchStage Stage = BatchStage.Matching;
    private const ModelTier Tier = ModelTier.Deep;

    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IMatchScopeQuery _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly IMatchRequestBuilder _requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
    private readonly IProfileRepository _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    private readonly ICvVersionRepository _cvVersions = cvVersions ?? throw new ArgumentNullException(nameof(cvVersions));
    private readonly ICostAccountant _accountant = accountant ?? throw new ArgumentNullException(nameof(accountant));
    private readonly ILlmBatchClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly ILogger<MatchingSubmitHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(
        EnrichmentCompleted message,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("EnrichmentCompleted for unknown Run {RunId}; ignoring.", message.RunId);
            return;
        }

        if (RunTransitions.IsTerminal(run.State))
        {
            _logger.LogInformation("Run {RunId} is terminal ({State}); nothing to match.", run.Id, run.State);
            return;
        }

        // Idempotency: a batch already recorded for this (run, Matching, Deep) means submission already
        // happened — the provider was paid once, and a redelivery must poll, never resubmit (AC-05, QG-1).
        var existingBatch = await _runs.FindBatchAsync(run.Id, Stage, Tier, cancellationToken).ConfigureAwait(false);
        if (existingBatch is not null)
        {
            _logger.LogInformation(
                "Run {RunId} already has matching batch {ProviderBatchId}; polling rather than resubmitting.",
                run.Id, existingBatch.ProviderBatchId);
            await bus.PublishAsync(new MatchingPollDue(run.Id)).ConfigureAwait(false);
            return;
        }

        if (run.State != RunState.Matching)
        {
            // No batch yet and not in Matching: a different stage owns this Run now; matching is not our call.
            _logger.LogInformation(
                "Run {RunId} is in {State} with no matching batch; submission is skipped.", run.Id, run.State);
            return;
        }

        // The CV crosses exactly one boundary — and it only does so if there is one. A Run with no active
        // Profile or no active CV cannot match anything, but it must not stall the pipeline: complete it
        // straight to Ranking with a zero-count MatchingCompleted so a (reduced) digest still ships.
        var profile = await _profiles.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        var cvVersion = profile is null
            ? null
            : await _cvVersions.FindActiveAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (profile is null || cvVersion is null)
        {
            await CompleteWithoutSubmittingAsync(
                run, bus, "no active CV or Profile to match against", cancellationToken).ConfigureAwait(false);
            return;
        }

        var jobs = await LoadScopeAsync(run, cancellationToken).ConfigureAwait(false);
        if (jobs.Count == 0)
        {
            // The scope emptied out (e.g. every job closed). Complete without spending — silence is worse
            // than a reduced digest (brief §9).
            await CompleteWithoutSubmittingAsync(
                run, bus, "empty matching scope at submission", cancellationToken).ConfigureAwait(false);
            return;
        }

        var request = _requestBuilder.Build(jobs, profile, cvVersion);
        var renderedPrompts = request.Items.Select(i => i.UserContent).ToList();
        var estimate = _accountant.Estimate(Tier, renderedPrompts, request.MaxOutputTokensPerItem);

        // The ceiling is a precondition, not an alarm: it is checked against what this Run has already spent
        // plus this estimate, and the client is not called at all when it would breach (QG-2, invariant 6).
        var projected = run.SpentUsd + estimate.CostUsd;
        if (projected > run.CeilingUsd)
        {
            await AbortOnCeilingAsync(run, estimate, projected, bus, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The estimate is written to the ledger and committed BEFORE the client is called, so the ceiling is
        // always checked against a ledger that already includes it. A resume that already committed the
        // estimate reuses it rather than double-counting (crash-matrix checkpoint 3, ADR-F3-0002).
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

        // Only now — after the estimate is durable — is the one spend-committing call made, guarded by
        // reconciliation so the single window where money could be spent without a record is closed (D5).
        var submission = new BatchSubmission(Tier, request.PromptVersion, request.Items);
        var providerBatchId = await ReconcileOrSubmitAsync(run, submission, alreadyEstimated, cancellationToken)
            .ConfigureAwait(false);

        // The provider id is persisted immediately, in the same transaction that records the batch and its
        // items. The unique index makes a second matching batch for this Run impossible, so a crash after
        // submit reconciles rather than repays. The Run stays in Matching — the poller (T06) advances it.
        var now = _clock.UtcNow;
        var batch = new Batch(
            _ids.NewId(), run.Id, Stage, Tier, providerBatchId, request.PromptVersion, request.Items.Count, now);
        _runs.AddBatch(batch);

        foreach (var item in request.Items)
        {
            var jobId = Guid.Parse(item.CustomId);
            _runs.AddBatchItem(new BatchItem(_ids.NewId(), batch.Id, item.CustomId, jobId));
        }

        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new MatchingBatchSubmitted(
            run.Id, batch.Id, providerBatchId, request.PromptVersion, request.Items.Count, now))
            .ConfigureAwait(false);
        await bus.PublishAsync(new MatchingPollDue(run.Id)).ConfigureAwait(false);

        _logger.LogInformation(
            "Submitted matching batch {ProviderBatchId} for Run {RunId}: {ItemCount} items, estimate {Estimate}.",
            providerBatchId, run.Id, request.Items.Count, estimate);
    }

    /// <summary>
    /// The D5 / crash-matrix checkpoint-4 mitigation, identical in shape to enrichment's: when a prior
    /// attempt is known to have reached at least the estimate commit (<paramref name="priorAttempt"/>), the
    /// client's recent batches are listed first, and an orphan created since this Run began is
    /// <em>adopted</em> rather than resubmitted so the provider is never paid twice. On a first attempt, or
    /// when no orphan is found, the one spend-committing call is made.
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

    private async Task<IReadOnlyList<MatchJobContent>> LoadScopeAsync(Run run, CancellationToken cancellationToken)
    {
        // The window's jobs plus the previous Run's failed items retrying once (AC-08) — the same scope
        // enrichment saw, so a job that made it into enrichment is matchable.
        var carriedOver = await _runs.FindRetriableJobIdsAsync(cancellationToken).ConfigureAwait(false);
        return await _scope
            .InScopeAsync(run.CutoffFrom, run.CutoffTo, carriedOver, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CompleteWithoutSubmittingAsync(
        Run run, IMessageBus bus, string reason, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var transition = run.TransitionTo(RunState.Ranking, now);
        if (transition.IsFailure)
        {
            // Programmer error — the Matching-state guard above should make this unreachable.
            throw new InvalidOperationException(
                $"Run {run.Id} could not move to Ranking after a no-op matching stage: {transition.Error.Message}");
        }

        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new MatchingCompleted(run.Id, Succeeded: 0, Failed: 0, CostUsd: 0m, now))
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Run {RunId} completed matching without spending ({Reason}); advanced to Ranking.", run.Id, reason);
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
            CultureInfo.InvariantCulture,
            $"Matching estimate ${estimate.CostUsd:0.0000} plus spend ${run.SpentUsd:0.0000} would breach the ${run.CeilingUsd:0.0000} ceiling.");

        run.Abort(reason, now, costBreach: true);
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new RunCostAborted(
            run.Id, estimate.CostUsd, run.CeilingUsd, run.SpentUsd, reason, now)).ConfigureAwait(false);

        _logger.LogWarning(
            "Run {RunId} cost-aborted before matching submission: projected ${Projected} over ceiling ${Ceiling}. The client was not called.",
            run.Id, projected, run.CeilingUsd);
    }
}
