using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Ratings;

/// <summary>
/// The one <see cref="IRegretMatcher"/> (F4 T21, ADR-F4-0003). It scores a sample of the pre-match filter's
/// <em>excluded</em> jobs at the cheap tier to falsify the filter: if any excluded job would have scored
/// well, a rule is wrong. It reuses the exact matching machinery a Run uses — the
/// <see cref="IMatchRequestBuilder"/> (the one boundary the CV crosses), the
/// <see cref="ILlmBatchClient"/> and the <see cref="IMatchResultParser"/> — but runs a self-contained
/// submit → poll → drain → parse loop at <see cref="ModelTier.Cheap"/> rather than going through the Run
/// pipeline, because a diagnostic sample has no Run, no ledger and no ceiling.
///
/// <para>The would-be score it returns is the model's raw <see cref="Match.MatchScore"/> — a 0–100 fit
/// judgement. That is deliberately conservative for a falsification control: the composed final score a Run
/// would show only ever multiplies this down (by an enrichment-missing discount and the learned weights),
/// so reporting the raw score errs toward over-alerting rather than hiding a genuine regret. The sampler
/// downstream is what thresholds it.</para>
///
/// <para>Whatever goes wrong — no active Profile or CV, a provider outage, a slow batch, a cancellation —
/// this <strong>returns what it has and never throws</strong>. A best-effort diagnostic must never surface
/// an exception into the weekly job; a missed sample is logged, not raised. The CV crosses exactly one
/// boundary here too: it is materialised only inside <see cref="IMatchRequestBuilder.Build"/>, never logged,
/// and the parsed results carry none of it.</para>
/// </summary>
public sealed class RegretMatcher(
    IProfileRepository profiles,
    ICvVersionRepository cvVersions,
    IMatchRequestBuilder requestBuilder,
    IMatchResultParser resultParser,
    ILlmBatchClient client,
    IClock clock,
    IIdGenerator ids,
    RegretMatchingOptions options,
    ILogger<RegretMatcher> logger) : IRegretMatcher
{
    private const ModelTier Tier = ModelTier.Cheap;

    private readonly IProfileRepository _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    private readonly ICvVersionRepository _cvVersions = cvVersions ?? throw new ArgumentNullException(nameof(cvVersions));
    private readonly IMatchRequestBuilder _requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
    private readonly IMatchResultParser _resultParser = resultParser ?? throw new ArgumentNullException(nameof(resultParser));
    private readonly ILlmBatchClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly RegretMatchingOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RegretMatcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RegretMatch>> MatchAsync(
        IReadOnlyList<MatchJobContent> jobs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        // An empty sample means the pre-match filter excluded nothing this week: nothing to falsify, no spend.
        if (jobs.Count == 0)
        {
            return [];
        }

        var profile = await _profiles.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            _logger.LogWarning("No active Profile; skipping the regret match. The client was not called.");
            return [];
        }

        var cvVersion = await _cvVersions.FindActiveAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (cvVersion is null)
        {
            _logger.LogWarning("No active CV version; skipping the regret match. The client was not called.");
            return [];
        }

        // The one CV boundary: the active CV's text enters here, folded into each item's user content, and
        // nowhere else. The custom_id on every item is the job id verbatim, so a result maps straight back.
        var request = _requestBuilder.Build(jobs, profile, cvVersion);

        // The whole submit-and-poll budget is a linked, self-cancelling token. When it elapses we abandon the
        // sample and answer with whatever parsed so far, so a stuck provider batch never hangs the weekly job.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        try
        {
            return await SubmitPollAndParseAsync(request, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "The regret match did not finish within the {Budget} budget; returning no regrets for the week.",
                _options.Timeout);
            return [];
        }
        catch (LlmBatchClientException ex)
        {
            _logger.LogWarning(ex, "The regret match failed at the provider; returning no regrets for the week.");
            return [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "The regret match failed at the transport level; returning no regrets for the week.");
            return [];
        }
    }

    /// <summary>
    /// Submits the one cheap-tier batch, polls it within the budget token, drains its results and parses each
    /// into a <see cref="RegretMatch"/> carrying the raw model score. A provider-side cancel/expire yields no
    /// regrets; a per-item provider error or a parse failure drops that one job rather than the whole sample.
    /// </summary>
    private async Task<IReadOnlyList<RegretMatch>> SubmitPollAndParseAsync(
        MatchBatchRequest request, CancellationToken budgetToken)
    {
        var submission = new BatchSubmission(Tier, request.PromptVersion, request.Items);
        var providerBatchId = await _client.SubmitAsync(submission, budgetToken).ConfigureAwait(false);
        _logger.LogInformation("Submitted regret-match batch {ProviderBatchId} of {Count} jobs at the cheap tier.",
            providerBatchId, request.Items.Count);

        var ended = await PollUntilEndedAsync(providerBatchId, budgetToken).ConfigureAwait(false);
        if (!ended)
        {
            _logger.LogWarning(
                "Regret-match batch {ProviderBatchId} did not end successfully; returning no regrets.", providerBatchId);
            return [];
        }

        var createdAt = _clock.UtcNow;
        var regrets = new List<RegretMatch>();
        await foreach (var result in _client.GetResultsAsync(providerBatchId, budgetToken).ConfigureAwait(false))
        {
            if (result.ProviderError is not null)
            {
                _logger.LogInformation(
                    "Regret-match item {CustomId} errored at the provider ({Error}); dropping it.",
                    result.CustomId, result.ProviderError);
                continue;
            }

            if (!Guid.TryParse(result.CustomId, out var jobId))
            {
                _logger.LogInformation("Regret-match result has an unparseable custom_id ({CustomId}); dropping it.",
                    result.CustomId);
                continue;
            }

            // The MatchParseRequest identity is throwaway — a regret match writes no Match aggregate — but the
            // parser needs a well-formed request, so we stamp fresh ids and the profile/CV the sample matched.
            var outcome = _resultParser.Parse(new MatchParseRequest(
                _ids.NewId(), jobId, _ids.NewId(), _ids.NewId(), _ids.NewId(),
                request.PromptVersion, createdAt, result.RawJson));

            if (outcome.IsSuccess)
            {
                regrets.Add(new RegretMatch(jobId, outcome.Match!.MatchScore));
            }
            else
            {
                _logger.LogInformation(
                    "Regret-match result for {JobId} did not parse ({Reason}); dropping it.",
                    jobId, outcome.FailureReason);
            }
        }

        return regrets;
    }

    /// <summary>
    /// Polls the batch until the provider reports it <see cref="ProviderBatchState.Ended"/> (returns true) or
    /// terminally cancelled/expired (returns false), sleeping <see cref="RegretMatchingOptions.PollInterval"/>
    /// between checks. The budget token bounds the whole loop.
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
}
