using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Reporting;

/// <summary>
/// The one <see cref="INarrativeSynthesizer"/> (F5 T05, ADR-F5-0001, Option A — bounded best-effort, inline).
/// It makes a single deep-tier synthesis call, priced and ceiling-checked and ledgered <em>exactly</em> like
/// every other batch (invariant 6), polls it within a short configurable budget, and returns the model note
/// if it lands in time — otherwise the deterministic <see cref="NarrativeTemplate"/>. It adds a stage
/// (<see cref="BatchStage.Synthesis"/>), not a mechanism: it reuses F3's <see cref="ILlmBatchClient"/>,
/// <see cref="ICostAccountant"/> and Run ledger unchanged.
///
/// <para>Whatever goes wrong — nothing to say, a ceiling breach, a provider outage, a slow batch, a
/// cancellation — this <strong>returns a <see cref="NarrativeResult"/>, never throws, and never blocks past
/// its budget</strong>. The market note is a nicety; a nicety must never delay or fail the 07:00 digest. On a
/// ceiling breach the client is <em>not called at all</em> (the estimate-plus-spend is checked first), which
/// is the invariant-6 precondition the tests assert as an absence.</para>
///
/// <para>No CV text is anywhere near this: the request is built from aggregate counts and one salary
/// statistic only (<see cref="NarrativeInput"/>), so the CV still crosses exactly one boundary — F4's match
/// prompt — and it is not this one.</para>
/// </summary>
public sealed class NarrativeSynthesizer(
    IRunRepository runs,
    INarrativeRequestBuilder requestBuilder,
    INarrativeResultParser resultParser,
    ICostAccountant accountant,
    ILlmBatchClient client,
    IClock clock,
    IIdGenerator ids,
    NarrativeSynthesisOptions options,
    ILogger<NarrativeSynthesizer> logger) : INarrativeSynthesizer
{
    private const BatchStage Stage = BatchStage.Synthesis;
    private const ModelTier Tier = ModelTier.Deep;

    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly INarrativeRequestBuilder _requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
    private readonly INarrativeResultParser _resultParser = resultParser ?? throw new ArgumentNullException(nameof(resultParser));
    private readonly ICostAccountant _accountant = accountant ?? throw new ArgumentNullException(nameof(accountant));
    private readonly ILlmBatchClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly NarrativeSynthesisOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<NarrativeSynthesizer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<NarrativeResult> SynthesizeAsync(
        Guid runId,
        NarrativeInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // A dead day has nothing to synthesise: the template says so for free and no model call — and no
        // spend — is made (ADR-F5-0001).
        if (!input.HasSomethingToSay)
        {
            _logger.LogInformation("Run {RunId} has nothing to synthesise; using the template note.", runId);
            return Template(input);
        }

        var run = await _runs.FindAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            // Defensive: the assembler only calls us for a Run it just loaded, but if it has gone the digest
            // must still ship with a template header rather than throw.
            _logger.LogWarning("Run {RunId} not found for narrative synthesis; using the template note.", runId);
            return Template(input);
        }

        var request = _requestBuilder.Build(input);
        var renderedPrompts = request.Items.Select(i => i.FullUserContent).ToList();
        var estimate = _accountant.Estimate(Tier, renderedPrompts, request.MaxOutputTokensPerItem);

        // The ceiling is a precondition, not an alarm (invariant 6): checked against what this Run has already
        // spent plus this estimate, and the client is NOT called at all when it would breach. A market note is
        // never worth breaching the ceiling for — fall back to the template, which costs nothing.
        var projected = run.SpentUsd + estimate.CostUsd;
        if (projected > run.CeilingUsd)
        {
            _logger.LogInformation(
                "Run {RunId} narrative estimate {Estimate} would breach the ${Ceiling} ceiling; using the template note. The client was not called.",
                runId, estimate, run.CeilingUsd);
            return Template(input);
        }

        // The estimate is written and committed BEFORE the client is called, so the ceiling is always checked
        // against a ledger that already includes it. A re-entry that already committed the estimate reuses it.
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

        // The whole submit-and-poll budget is a linked, self-cancelling token: when it elapses we abandon the
        // model note and answer from the template, so the digest is never delayed (ADR-F5-0001). Everything
        // from here on is best-effort — no failure escapes as an exception.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        try
        {
            var note = await SubmitPollAndParseAsync(run, request, budget.Token).ConfigureAwait(false);
            return note is null
                ? Template(input)
                : NarrativeResult.Model(note, request.PromptVersion);
        }
        catch (OperationCanceledException)
        {
            // Our budget fired (a slow batch) or the caller is aborting the whole assembly window. Either way
            // the note is optional and the digest still ships: answer from the template rather than propagate.
            _logger.LogInformation(
                "Run {RunId} narrative synthesis did not finish within the {Budget} budget; using the template note.",
                runId, _options.Timeout);
            return Template(input);
        }
        catch (LlmBatchClientException ex)
        {
            // The provider's declared fault (a 4xx/5xx surfaced by the adapter). A market note is never worth
            // failing the digest for, so record it and answer from the template.
            _logger.LogWarning(
                ex, "Run {RunId} narrative synthesis failed at the provider; using the template note.", runId);
            return Template(input);
        }
        catch (HttpRequestException ex)
        {
            // A transport fault — DNS, connection reset, TLS — that reached us before the adapter wrapped it.
            _logger.LogWarning(
                ex, "Run {RunId} narrative synthesis failed at the transport level; using the template note.", runId);
            return Template(input);
        }
    }

    /// <summary>
    /// Submits the one-item synthesis batch (or adopts an already-recorded one on a re-entry), polls it within
    /// the caller's budget token, and parses the single result. Returns the trimmed note on success, or null
    /// for any best-effort miss — a provider-side cancel/expire, an item error, or a parse failure — which the
    /// caller turns into the template fallback. On success it also writes the <see cref="LedgerEntryKind.Actual"/>
    /// ledger entry and completes the batch, so a spent synthesis is accounted for like any other.
    /// </summary>
    private async Task<string?> SubmitPollAndParseAsync(
        Run run, NarrativeBatchRequest request, CancellationToken budgetToken)
    {
        var batch = await SubmitOrAdoptAsync(run, request, budgetToken).ConfigureAwait(false);

        var ended = await PollUntilEndedAsync(batch.ProviderBatchId, budgetToken).ConfigureAwait(false);
        if (!ended)
        {
            // Provider-side cancelled or expired: nothing to retrieve, and a nicety is not worth a retry.
            _logger.LogInformation(
                "Run {RunId} synthesis batch {ProviderBatchId} did not end successfully; using the template note.",
                run.Id, batch.ProviderBatchId);
            return null;
        }

        string? note = null;
        var inputTokens = 0;
        var outputTokens = 0;
        await foreach (var result in _client.GetResultsAsync(batch.ProviderBatchId, budgetToken).ConfigureAwait(false))
        {
            inputTokens += result.Usage.InputTokens;
            outputTokens += result.Usage.OutputTokens;

            if (result.ProviderError is not null)
            {
                _logger.LogInformation(
                    "Run {RunId} synthesis item errored at the provider ({Error}); using the template note.",
                    run.Id, result.ProviderError);
                continue;
            }

            var outcome = _resultParser.Parse(result.RawJson);
            if (outcome.IsSuccess)
            {
                note = outcome.Narrative;
            }
            else
            {
                _logger.LogInformation(
                    "Run {RunId} synthesis result did not parse ({Reason}); using the template note.",
                    run.Id, outcome.FailureReason);
            }
        }

        // The batch was submitted, so it was billed: write the Actual entry and complete it even when the note
        // itself did not parse — the spend happened regardless of whether we could use the output.
        await WriteActualCostAsync(run, batch, inputTokens, outputTokens, budgetToken).ConfigureAwait(false);

        return note;
    }

    /// <summary>
    /// Returns the synthesis batch, submitting one when none exists yet or adopting the one a prior attempt
    /// already recorded (idempotency: the unique <c>(run, Synthesis, Deep)</c> index makes a second submission
    /// impossible, so a re-entry polls rather than pays again).
    /// </summary>
    private async Task<Batch> SubmitOrAdoptAsync(
        Run run, NarrativeBatchRequest request, CancellationToken budgetToken)
    {
        var existing = await _runs.FindBatchAsync(run.Id, Stage, Tier, budgetToken).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Run {RunId} already has a synthesis batch {ProviderBatchId}; polling rather than resubmitting.",
                run.Id, existing.ProviderBatchId);
            return existing;
        }

        var submission = new BatchSubmission(Tier, request.PromptVersion, request.Items);
        var providerBatchId = await _client.SubmitAsync(submission, budgetToken).ConfigureAwait(false);

        var now = _clock.UtcNow;
        var batch = new Batch(
            _ids.NewId(), run.Id, Stage, Tier, providerBatchId, request.PromptVersion, request.Items.Count, now);
        _runs.AddBatch(batch);

        // A synthesis batch persists no BatchItem rows. Unlike enrichment and matching, a market note is
        // best-effort and job-less: it is never retried, never carried over, and the F3 poller never touches
        // it — the whole submit/poll/retrieve/complete cycle runs inline here. A BatchItem exists to give one
        // job's request per-item failure isolation and a single retry (AC-08); a note that references no job
        // has nothing for it to carry, and the Domain invariant rightly forbids a job-less item. The Batch row
        // alone is the idempotency key (the unique (run, Synthesis, Deep) index), which is all a re-entry needs.
        await _runs.SaveChangesAsync(budgetToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Submitted synthesis batch {ProviderBatchId} for Run {RunId}.", providerBatchId, run.Id);
        return batch;
    }

    /// <summary>
    /// Polls the batch until the provider reports it <see cref="ProviderBatchState.Ended"/> (returns true) or
    /// terminally cancelled/expired (returns false), sleeping <see cref="NarrativeSynthesisOptions.PollInterval"/>
    /// between checks. The budget token bounds the whole loop: when it elapses the sleep or the status call
    /// throws <see cref="OperationCanceledException"/>, which the caller turns into the template fallback.
    /// </summary>
    private async Task<bool> PollUntilEndedAsync(string providerBatchId, CancellationToken budgetToken)
    {
        while (true)
        {
            budgetToken.ThrowIfCancellationRequested();

            var status = await _client.GetStatusAsync(providerBatchId, budgetToken).ConfigureAwait(false);
            switch (status.State)
            {
                case ProviderBatchState.Ended:
                    return true;
                case ProviderBatchState.Cancelled:
                case ProviderBatchState.Expired:
                    return false;
                default:
                    await Task.Delay(_options.PollInterval, budgetToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>
    /// Writes the <see cref="LedgerEntryKind.Actual"/> entry from the provider's reported usage and completes
    /// the batch, guarded so a re-entry writes it at most once (mirrors matching's <c>WriteActualCostAsync</c>).
    /// </summary>
    private async Task WriteActualCostAsync(
        Run run, Batch batch, int inputTokens, int outputTokens, CancellationToken cancellationToken)
    {
        var alreadyActual = await _runs
            .HasLedgerEntryAsync(run.Id, Stage, Tier, LedgerEntryKind.Actual, cancellationToken)
            .ConfigureAwait(false);
        if (!alreadyActual)
        {
            var actual = _accountant.Actual(Tier, inputTokens, outputTokens);
            _runs.AddLedgerEntry(new CostLedgerEntry(
                _ids.NewId(), run.Id, batch.Id, Stage, Tier, LedgerEntryKind.Actual,
                actual.CostUsd, actual.InputTokens, actual.OutputTokens, _clock.UtcNow));
        }

        if (batch.State is not (BatchState.Completed or BatchState.Failed or BatchState.Expired))
        {
            batch.TransitionTo(BatchState.Completed, _clock.UtcNow, inputTokens, outputTokens);
        }

        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static NarrativeResult Template(NarrativeInput input) =>
        NarrativeResult.Template(NarrativeTemplate.Render(input));
}
