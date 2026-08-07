using System.Globalization;
using JobHunter.Application.Enrichment;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/run</c> (catalogue §Operations · Sensitive · ✎): triggers the daily pipeline off its 07:00 schedule, for the
/// day the Owner wants a Run now rather than tomorrow. Refused outright while a Run is live — the pipeline holds one
/// live Run at a time (the orchestrator refuses a second, ADR-F3-0002), so this checks <see cref="IRunRepository.FindActiveRunAsync"/>
/// and states the refusal rather than starting a rival Run.
///
/// <para>Otherwise it previews rather than acts. It reproduces the scope the orchestrator would take — the live jobs
/// first seen since the last Run's cut-off, or the initial look-back when no previous Run exists — and names the
/// honest cost cap, the <see cref="RunOptions.CeilingUsd"/> every Run is snapshotted under. No pre-submission cost
/// <em>estimate</em> is possible here: that needs the rendered prompts the Worker builds, so the ceiling is the true
/// figure to show (invariant 6 is enforced Worker-side, before submission). It then stores a short-lived per-chat
/// <see cref="ConversationState"/> awaiting confirmation and asks. No LLM, no CV.</para>
///
/// <para><strong>Deferred to T10.</strong> The confirm tap that publishes <c>StartDailyRun</c> is wired with the
/// dispatch rewire (T10) — the Telegram host does not run Wolverine, so the <c>IMessageBus</c> that publishes the
/// trigger is not yet in its container. This task previews and asks, the same convention <c>/floor</c> follows.</para>
/// </summary>
internal sealed class RunCommandHandler(
    IRunRepository runs,
    ILiveJobsQuery liveJobs,
    IConversationStateStore state,
    IClock clock,
    RunOptions options,
    ILogger<RunCommandHandler> logger) : ICommandHandler
{
    /// <summary>The registry name a pending state carries, so the resume step (T10) knows which command to resume.</summary>
    private const string CommandName = "run";

    /// <summary>The step the flow waits for — the Owner's confirmation of the previewed off-schedule Run.</summary>
    private const string AwaitingConfirm = "confirm";

    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly ILiveJobsQuery _liveJobs = liveJobs ?? throw new ArgumentNullException(nameof(liveJobs));
    private readonly IConversationStateStore _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly RunOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RunCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // One live Run at a time: if the pipeline is already working, refuse and touch nothing. The orchestrator
        // refuses a second Run on the write side too; this just says so before anything is previewed or stored.
        var live = await _runs.FindActiveRunAsync(cancellationToken).ConfigureAwait(false);
        if (live is not null)
        {
            _logger.LogDebug("/run refused: a Run is already live ({RunId}).", live.Id);
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("A run is already in progress — wait for it to finish.") + "_")];
        }

        // Reproduce the scope the orchestrator would take: jobs first seen since the previous Run's cut-off, or the
        // initial look-back when there is no previous Run to inherit a cut-off from (data-model §runs).
        var now = _clock.UtcNow;
        var since = await _runs.FindMostRecentCutoffAsync(cancellationToken).ConfigureAwait(false)
            ?? now - _options.InitialLookBack;
        var discovered = await _liveJobs.DiscoveredSinceAsync(since, cancellationToken).ConfigureAwait(false);
        var inScope = discovered.Count(j => j.FirstSeenAt <= now);

        // Preview and ask: store the pending confirm state, then name the scope and the honest cost cap. Nothing is
        // published here — the confirm tap publishes StartDailyRun in T10, the same convention /floor's confirm follows.
        var pending = new ConversationState(CommandName, AwaitingConfirm, context: null, now);
        await _state.SetAsync(request.ChatId, pending, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "/run previewed an off-schedule Run: {InScope} job(s) in scope under a {Ceiling:0.00} USD ceiling.",
            inScope, _options.CeilingUsd);

        var jobWord = inScope == 1 ? "job" : "jobs";
        var ceiling = _options.CeilingUsd.ToString("0.00", CultureInfo.InvariantCulture);
        return [RenderedMessage.PlainText(MarkdownV2Escaper.Escape(
            $"An off-schedule run would analyse {inScope} {jobWord} in scope, under a ${ceiling} cost ceiling. "
            + "Reply confirm to start it, or /cancel to stop."))];
    }
}
