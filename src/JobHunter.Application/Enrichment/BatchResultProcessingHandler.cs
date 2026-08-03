using System.Globalization;
using JobHunter.Application.Common;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Enrichment;

/// <summary>
/// The result-processing step (F3 SAD §6.2, T12). It consumes <see cref="BatchResultsReady"/> — published
/// by the poller the moment the provider batch ended — streams the results one item at a time, parses each
/// <strong>independently</strong>, upserts the valid ones as enrichments and records the bad ones with
/// their raw payload and error, writes the <see cref="LedgerEntryKind.Actual"/> cost entry from the
/// provider's reported usage, and advances the Run to <see cref="RunState.Matching"/> (AC-07, AC-10, QG-3).
///
/// <para>Everything here is idempotent, because a crash mid-processing must not double-charge or duplicate
/// a row (crash-matrix checkpoints 7 and 8, AC-06). Enrichments upsert on the unique <c>(job_id, run_id)</c>
/// index — <c>ON CONFLICT DO NOTHING</c> — so replaying a half-stored result set writes each exactly once.
/// The <c>Actual</c> ledger entry is written at most once, guarded by <see cref="IRunRepository.HasLedgerEntryAsync"/>
/// so a reprocess adds no extra cost. The Run's move to <c>Matching</c> and the batch's move to
/// <see cref="BatchState.Completed"/> are legal-once transitions — a resume that already advanced finds a
/// terminal edge and no-ops. The handler never throws for a single bad item: one malformed result is one
/// <see cref="BatchItemState.ParseFailed"/> row, not a failed batch, and the item retries once next Run (AC-08).</para>
/// </summary>
public sealed class BatchResultProcessingHandler(
    IRunRepository runs,
    IEnrichmentRepository enrichments,
    IEnrichmentResultParser parser,
    ILlmBatchClient client,
    ICostAccountant accountant,
    IClock clock,
    IIdGenerator ids,
    ILogger<BatchResultProcessingHandler> logger)
{
    private const BatchStage Stage = BatchStage.Enrichment;
    private const ModelTier Tier = ModelTier.Cheap;

    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IEnrichmentRepository _enrichments = enrichments ?? throw new ArgumentNullException(nameof(enrichments));
    private readonly IEnrichmentResultParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    private readonly ILlmBatchClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly ICostAccountant _accountant = accountant ?? throw new ArgumentNullException(nameof(accountant));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly ILogger<BatchResultProcessingHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(BatchResultsReady message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("BatchResultsReady for unknown Run {RunId}; ignoring.", message.RunId);
            return;
        }

        if (RunTransitions.IsTerminal(run.State))
        {
            _logger.LogInformation("Run {RunId} is terminal ({State}); results already processed.", run.Id, run.State);
            return;
        }

        var batch = await _runs.FindBatchAsync(run.Id, Stage, Tier, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            _logger.LogWarning("Run {RunId} has no enrichment batch; nothing to process.", run.Id);
            return;
        }

        if (batch.State is BatchState.Completed or BatchState.Failed or BatchState.Expired)
        {
            // A redelivery after the batch already completed: the Run may still need to advance if the crash
            // fell between the batch commit and the Run move (checkpoint 8). Advancing is itself idempotent.
            _logger.LogInformation(
                "Batch {ProviderBatchId} is already {State}; ensuring the Run has advanced.", batch.ProviderBatchId, batch.State);
            await AdvanceRunAsync(run, bus, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Index the items by custom id (the job id verbatim) so each streamed result maps back with no
        // lookup table (SAD §8). Tracked entities, so state changes commit with the batch and Run below.
        var items = await _runs.FindBatchItemsAsync(batch.Id, cancellationToken).ConfigureAwait(false);
        var itemsByCustomId = items.ToDictionary(i => i.CustomId, StringComparer.Ordinal);

        var enrichedCount = 0;
        var failedCount = 0;
        var inputTokens = 0;
        var outputTokens = 0;

        await foreach (var result in _client.GetResultsAsync(batch.ProviderBatchId, cancellationToken).ConfigureAwait(false))
        {
            // Token usage is per-item as reported; summing gives the batch actual regardless of ordering.
            inputTokens += result.Usage.InputTokens;
            outputTokens += result.Usage.OutputTokens;

            if (!itemsByCustomId.TryGetValue(result.CustomId, out var item))
            {
                // A result for an item we never submitted — log and skip rather than throw; the batch's own
                // items are the source of truth for what the Run owes an assessment.
                _logger.LogWarning(
                    "Batch {ProviderBatchId} returned an unexpected custom_id {CustomId}; skipping.",
                    batch.ProviderBatchId, result.CustomId);
                continue;
            }

            if (await ProcessItemAsync(run, batch, item, result, cancellationToken).ConfigureAwait(false))
            {
                enrichedCount++;
            }
            else
            {
                failedCount++;
            }
        }

        // AC-10: the actual cost is priced from the provider's reported usage and attributed to this batch,
        // stage and tier. Written at most once — a reprocess after a crash finds the entry and adds nothing.
        await WriteActualCostAsync(run, batch, inputTokens, outputTokens, cancellationToken).ConfigureAwait(false);

        batch.TransitionTo(BatchState.Completed, _clock.UtcNow, inputTokens, outputTokens);
        run.RecordCarryOver(failedCount);
        var advanced = run.TransitionTo(RunState.Matching, _clock.UtcNow);
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (advanced.IsSuccess)
        {
            await bus.PublishAsync(new EnrichmentCompleted(run.Id, enrichedCount, failedCount, _clock.UtcNow))
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Processed enrichment batch {ProviderBatchId} for Run {RunId}: {Enriched} stored, {Failed} failed.",
            batch.ProviderBatchId, run.Id, enrichedCount, failedCount);
    }

    /// <summary>
    /// Parses and stores one item, returning true when an enrichment was stored (or was already present on a
    /// replay). A parse failure or a provider error records the item with its raw payload and reason for the
    /// one retry next Run (AC-07, AC-08). Never throws for a single item (QG-3).
    /// </summary>
    private async Task<bool> ProcessItemAsync(
        Run run, Batch batch, BatchItem item, BatchResultItem result, CancellationToken cancellationToken)
    {
        if (item.State is BatchItemState.Parsed)
        {
            // A replay of an item stored on the earlier pass: the enrichment is already durable on its unique
            // key, so re-count it as stored and do not re-upsert (checkpoint 7, AC-06).
            return true;
        }

        if (result.ProviderError is not null)
        {
            item.MarkProviderError(result.ProviderError, result.RawJson);
            return false;
        }

        var outcome = _parser.Parse(new EnrichmentParseRequest(
            _ids.NewId(), item.JobId, run.Id, batch.PromptVersion, _clock.UtcNow, result.RawJson));

        if (!outcome.IsSuccess)
        {
            item.MarkParseFailed(outcome.FailureReason!, result.RawJson);

            // Instrument #8: an item the provider returned but that failed schema validation (observability
            // §2). Labelled by stage and tier only — never the job id — so cardinality stays bounded.
            Telemetry.ParseFailures.Add(
                1,
                new KeyValuePair<string, object?>(TelemetryLabels.Stage, Stage.ToString()),
                new KeyValuePair<string, object?>(TelemetryLabels.Tier, Tier.ToString()));
            return false;
        }

        // Upsert on (job_id, run_id): ON CONFLICT DO NOTHING makes a replay a no-op, so a half-stored set
        // reprocessed writes each enrichment exactly once (AC-06, invariant 3).
        await _enrichments.UpsertAsync(outcome.Enrichment!, cancellationToken).ConfigureAwait(false);
        item.MarkParsed();

        if (outcome.Anomalies.Count > 0)
        {
            _logger.LogInformation(
                "Enrichment for job {JobId} carried {Count} repaired anomaly(ies).", item.JobId, outcome.Anomalies.Count);
        }

        return true;
    }

    private async Task WriteActualCostAsync(
        Run run, Batch batch, int inputTokens, int outputTokens, CancellationToken cancellationToken)
    {
        var alreadyActual = await _runs
            .HasLedgerEntryAsync(run.Id, Stage, Tier, LedgerEntryKind.Actual, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyActual)
        {
            return;
        }

        var actual = _accountant.Actual(Tier, inputTokens, outputTokens);
        _runs.AddLedgerEntry(new CostLedgerEntry(
            _ids.NewId(), run.Id, batch.Id, Stage, Tier, LedgerEntryKind.Actual,
            actual.CostUsd, actual.InputTokens, actual.OutputTokens, _clock.UtcNow));

        // The denormalised SpentUsd retains the pre-submission estimate; the ledger is authoritative for
        // attribution (data-model §cost_ledger_entries, §runs). Keeping the pessimistic estimate rather than
        // reconciling it down is the safe direction the ceiling relies on (ADR-F3-0002).

        // Instruments #2 and #5 are recorded here, inside the same once-only ledger guard, so a reprocess
        // after a crash (checkpoints 7-8) adds no duplicate cost point and no duplicate latency point — the
        // metric totals match an uninterrupted Run exactly (observability §2). Labelled by stage and tier
        // only; the run id lives on spans, never on a metric label.
        Telemetry.RunCost.Record(
            (double)actual.CostUsd,
            new KeyValuePair<string, object?>(TelemetryLabels.Stage, Stage.ToString()),
            new KeyValuePair<string, object?>(TelemetryLabels.Tier, Tier.ToString()));

        var latency = _clock.UtcNow - batch.SubmittedAt;
        Telemetry.BatchLatency.Record(
            latency.TotalSeconds,
            new KeyValuePair<string, object?>(TelemetryLabels.Stage, Stage.ToString()),
            new KeyValuePair<string, object?>(TelemetryLabels.Tier, Tier.ToString()));
    }

    private async Task AdvanceRunAsync(Run run, IMessageBus bus, CancellationToken cancellationToken)
    {
        var advanced = run.TransitionTo(RunState.Matching, _clock.UtcNow);
        if (!advanced.IsSuccess)
        {
            return;
        }

        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await bus.PublishAsync(new EnrichmentCompleted(run.Id, run.JobsInScope - run.JobsCarriedOver, run.JobsCarriedOver, _clock.UtcNow))
            .ConfigureAwait(false);
    }
}
