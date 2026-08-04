using JobHunter.Application.Common;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Matching;

/// <summary>
/// The matching result-processing step (F4 SAD §6.1, T06). The deep-tier twin of F3's
/// <c>BatchResultProcessingHandler</c>: it consumes <see cref="MatchingResultsReady"/> — published by the
/// matching poller the moment the provider batch ended — streams the results one item at a time, parses
/// each <strong>independently</strong>, upserts the valid ones as matches and records the bad ones with
/// their raw payload and error, writes the <see cref="LedgerEntryKind.Actual"/> cost entry from the
/// provider's reported usage, and advances the Run to <see cref="RunState.Ranking"/> (AC-02, AC-10).
///
/// <para>Every match is stamped with the active profile and CV version, so re-staling can find and retire
/// it when the CV is superseded (AC-08). No CV text crosses this boundary: the parser sees only the model's
/// output, and the stamped ids are opaque references, not content.</para>
///
/// <para>Everything here is idempotent, because a crash mid-processing must not double-charge or duplicate
/// a row. Matches upsert on the unique <c>(job_id, run_id, profile_id)</c> index — a replay of a
/// half-stored result set writes each exactly once (invariant 3). The <c>Actual</c> ledger entry is written
/// at most once, guarded by <see cref="IRunRepository.HasLedgerEntryAsync"/>. The Run's move to
/// <c>Ranking</c> and the batch's move to <see cref="BatchState.Completed"/> are legal-once transitions — a
/// resume that already advanced finds a terminal edge and no-ops. The handler never throws for a single bad
/// item: one malformed result is one <see cref="BatchItemState.ParseFailed"/> row, not a failed batch, and
/// a match with no reasons is recorded failed rather than persisted (AC-02, QG-3). A Run whose active
/// Profile or CV has since gone completes to Ranking with a zero count rather than stalling.</para>
/// </summary>
public sealed class MatchingResultProcessingHandler(
    IRunRepository runs,
    IMatchRepository matches,
    IMatchResultParser parser,
    IProfileRepository profiles,
    ICvVersionRepository cvVersions,
    ILlmBatchClient client,
    ICostAccountant accountant,
    IClock clock,
    IIdGenerator ids,
    ILogger<MatchingResultProcessingHandler> logger)
{
    private const BatchStage Stage = BatchStage.Matching;
    private const ModelTier Tier = ModelTier.Deep;

    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IMatchRepository _matches = matches ?? throw new ArgumentNullException(nameof(matches));
    private readonly IMatchResultParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    private readonly IProfileRepository _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    private readonly ICvVersionRepository _cvVersions = cvVersions ?? throw new ArgumentNullException(nameof(cvVersions));
    private readonly ILlmBatchClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly ICostAccountant _accountant = accountant ?? throw new ArgumentNullException(nameof(accountant));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly ILogger<MatchingResultProcessingHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(MatchingResultsReady message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("MatchingResultsReady for unknown Run {RunId}; ignoring.", message.RunId);
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
            _logger.LogWarning("Run {RunId} has no matching batch; nothing to process.", run.Id);
            return;
        }

        if (batch.State is BatchState.Completed or BatchState.Failed or BatchState.Expired)
        {
            // A redelivery after the batch already completed: the Run may still need to advance if the crash
            // fell between the batch commit and the Run move. Advancing is itself idempotent.
            _logger.LogInformation(
                "Matching batch {ProviderBatchId} is already {State}; ensuring the Run has advanced.",
                batch.ProviderBatchId, batch.State);
            await AdvanceRunAsync(run, bus, 0m, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The match rows must be stamped with the profile and CV version they were made against (AC-08). If
        // either has gone since submission there is nothing to stamp against, but the Run must not stall:
        // complete it to Ranking with a zero count so a reduced digest still ships.
        var profile = await _profiles.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        var cvVersion = profile is null
            ? null
            : await _cvVersions.FindActiveAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (profile is null || cvVersion is null)
        {
            _logger.LogWarning(
                "Run {RunId} matching results arrived with no active Profile or CV to stamp; completing to Ranking with a zero count.",
                run.Id);
            batch.TransitionTo(BatchState.Completed, _clock.UtcNow);
            await AdvanceRunAsync(run, bus, 0m, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Index the items by custom id (the job id verbatim) so each streamed result maps back with no
        // lookup table. Tracked entities, so state changes commit with the batch and Run below.
        var items = await _runs.FindBatchItemsAsync(batch.Id, cancellationToken).ConfigureAwait(false);
        var itemsByCustomId = items.ToDictionary(i => i.CustomId, StringComparer.Ordinal);

        var matchedCount = 0;
        var failedCount = 0;
        var inputTokens = 0;
        var outputTokens = 0;

        await foreach (var result in _client.GetResultsAsync(batch.ProviderBatchId, cancellationToken).ConfigureAwait(false))
        {
            inputTokens += result.Usage.InputTokens;
            outputTokens += result.Usage.OutputTokens;

            if (!itemsByCustomId.TryGetValue(result.CustomId, out var item))
            {
                _logger.LogWarning(
                    "Matching batch {ProviderBatchId} returned an unexpected custom_id {CustomId}; skipping.",
                    batch.ProviderBatchId, result.CustomId);
                continue;
            }

            if (await ProcessItemAsync(run, batch, profile.Id, cvVersion.Id, item, result, cancellationToken).ConfigureAwait(false))
            {
                matchedCount++;
            }
            else
            {
                failedCount++;
            }
        }

        var actualCost = await WriteActualCostAsync(run, batch, inputTokens, outputTokens, cancellationToken).ConfigureAwait(false);

        batch.TransitionTo(BatchState.Completed, _clock.UtcNow, inputTokens, outputTokens);
        run.RecordCarryOver(failedCount);
        var advanced = run.TransitionTo(RunState.Ranking, _clock.UtcNow);
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (advanced.IsSuccess)
        {
            await bus.PublishAsync(new MatchingCompleted(run.Id, matchedCount, failedCount, actualCost, _clock.UtcNow))
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Processed matching batch {ProviderBatchId} for Run {RunId}: {Matched} stored, {Failed} failed.",
            batch.ProviderBatchId, run.Id, matchedCount, failedCount);
    }

    /// <summary>
    /// Parses and stores one item, returning true when a match was stored (or was already present on a
    /// replay). A parse failure or a provider error records the item with its raw payload and reason for the
    /// one retry next Run. A match with no reasons never reaches the database — the tolerant parser rejects
    /// it, so it is a recorded failure, not a persisted match (AC-02). Never throws for a single item (QG-3).
    /// </summary>
    private async Task<bool> ProcessItemAsync(
        Run run,
        Batch batch,
        Guid profileId,
        Guid cvVersionId,
        BatchItem item,
        BatchResultItem result,
        CancellationToken cancellationToken)
    {
        if (item.State is BatchItemState.Parsed)
        {
            // A replay of an item stored on the earlier pass: the match is already durable on its unique key,
            // so re-count it as stored and do not re-upsert (invariant 3).
            return true;
        }

        if (result.ProviderError is not null)
        {
            item.MarkProviderError(result.ProviderError, result.RawJson);
            return false;
        }

        var outcome = _parser.Parse(new MatchParseRequest(
            _ids.NewId(), item.JobId, run.Id, profileId, cvVersionId, batch.PromptVersion, _clock.UtcNow, result.RawJson));

        if (!outcome.IsSuccess)
        {
            item.MarkParseFailed(outcome.FailureReason!, result.RawJson);
            Telemetry.ParseFailures.Add(
                1,
                new KeyValuePair<string, object?>(TelemetryLabels.Stage, Stage.ToString()),
                new KeyValuePair<string, object?>(TelemetryLabels.Tier, Tier.ToString()));
            return false;
        }

        // Upsert on (job_id, run_id, profile_id): a replay is a no-op, so a half-stored set reprocessed
        // writes each match exactly once (invariant 3).
        await _matches.UpsertAsync(outcome.Match!, cancellationToken).ConfigureAwait(false);
        item.MarkParsed();

        if (outcome.Anomalies.Count > 0)
        {
            _logger.LogInformation(
                "Match for job {JobId} carried {Count} repaired anomaly(ies).", item.JobId, outcome.Anomalies.Count);
        }

        return true;
    }

    private async Task<decimal> WriteActualCostAsync(
        Run run, Batch batch, int inputTokens, int outputTokens, CancellationToken cancellationToken)
    {
        var alreadyActual = await _runs
            .HasLedgerEntryAsync(run.Id, Stage, Tier, LedgerEntryKind.Actual, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyActual)
        {
            return 0m;
        }

        var actual = _accountant.Actual(Tier, inputTokens, outputTokens);
        _runs.AddLedgerEntry(new CostLedgerEntry(
            _ids.NewId(), run.Id, batch.Id, Stage, Tier, LedgerEntryKind.Actual,
            actual.CostUsd, actual.InputTokens, actual.OutputTokens, _clock.UtcNow));

        Telemetry.RunCost.Record(
            (double)actual.CostUsd,
            new KeyValuePair<string, object?>(TelemetryLabels.Stage, Stage.ToString()),
            new KeyValuePair<string, object?>(TelemetryLabels.Tier, Tier.ToString()));

        var latency = _clock.UtcNow - batch.SubmittedAt;
        Telemetry.BatchLatency.Record(
            latency.TotalSeconds,
            new KeyValuePair<string, object?>(TelemetryLabels.Stage, Stage.ToString()),
            new KeyValuePair<string, object?>(TelemetryLabels.Tier, Tier.ToString()));

        return actual.CostUsd;
    }

    private async Task AdvanceRunAsync(Run run, IMessageBus bus, decimal costUsd, CancellationToken cancellationToken)
    {
        var advanced = run.TransitionTo(RunState.Ranking, _clock.UtcNow);
        if (!advanced.IsSuccess)
        {
            return;
        }

        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await bus.PublishAsync(new MatchingCompleted(
            run.Id, run.JobsInScope - run.JobsCarriedOver, run.JobsCarriedOver, costUsd, _clock.UtcNow))
            .ConfigureAwait(false);
    }
}
