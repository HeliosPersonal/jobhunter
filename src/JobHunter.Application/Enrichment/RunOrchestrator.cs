using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Enrichment;

/// <summary>
/// Opens and resumes the daily Run — the start, scope and resume half of the Run machinery (F3 SAD §6.1,
/// ADR-F3-0001, T09). Submission, polling and result processing are separate stateless steps (T10–T12);
/// this type only creates a Run, selects its scope, and re-enters a non-terminal Run at its current
/// state after a restart.
///
/// <para>Two behaviours carry the feature's resumability (QG-1). First, scope is
/// <em>the discovery window</em>: <c>cutoff_from</c> is the previous Run's <c>cutoff_to</c> (so a
/// skipped day is caught up rather than lost, data-model §runs) and the window's jobs are counted once —
/// <see cref="Run.SetScope"/> is idempotent, so a resume that recomputes the scope converges. Second, a
/// resumed Run is dispatched on its persisted <see cref="Run.State"/>: a <see cref="RunState.Created"/>
/// Run continues to submission (or, with an empty scope, completes without spending), and an
/// <see cref="RunState.Enriching"/> Run is <em>polled</em>, never resubmitted (AC-05) — the already-paid
/// batch is reached through its persisted provider id, not bought again.</para>
///
/// <para>A zero-scope Run completes the enrichment stage without submitting anything and without error —
/// silence is indistinguishable from breakage, so a digest still ships downstream (brief §9). The
/// active-Run guard is belt-and-suspenders over the partial unique index that is the hard arbiter of
/// "one live Run" (data-model §runs): the orchestrator declines to create a second, and the database
/// would reject it anyway.</para>
/// </summary>
public sealed class RunOrchestrator(
    IRunRepository runs,
    ILiveJobsQuery liveJobs,
    IClock clock,
    IIdGenerator ids,
    ILogger<RunOrchestrator> logger)
{
    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly ILiveJobsQuery _liveJobs = liveJobs ?? throw new ArgumentNullException(nameof(liveJobs));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly ILogger<RunOrchestrator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Opens the day's Run: derives the window from the previous Run's <c>cutoff_to</c>, snapshots the
    /// ceiling, selects the scope, and either hands off to submission or completes an empty day. A live
    /// Run already existing makes this a no-op — a redelivered tick does not start a second Run.
    /// </summary>
    public async Task Handle(
        StartDailyRun message,
        IMessageBus bus,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(options);

        var existing = await _runs.FindActiveRunAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation(
                "A live Run {RunId} already exists ({State}); StartDailyRun is a no-op.",
                existing.Id, existing.State);
            return;
        }

        var cutoffTo = message.WindowEnd;
        var previousCutoff = await _runs.FindMostRecentCutoffAsync(cancellationToken).ConfigureAwait(false);
        var cutoffFrom = previousCutoff ?? cutoffTo - options.InitialLookBack;

        var run = new Run(_ids.NewId(), cutoffFrom, cutoffTo, options.CeilingUsd, _clock.UtcNow);
        _runs.Add(run);

        var scope = await SelectScopeAsync(cutoffFrom, cutoffTo, cancellationToken).ConfigureAwait(false);
        run.SetScope(scope);

        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new RunStarted(
            run.Id, run.CutoffFrom, run.CutoffTo, run.JobsInScope, run.CeilingUsd, _clock.UtcNow))
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Started Run {RunId} for window {From:o}..{To:o} with {Scope} job(s) in scope.",
            run.Id, run.CutoffFrom, run.CutoffTo, run.JobsInScope);

        await ContinueFromCreatedAsync(run, bus, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The startup resume sweep (QG-1, AC-05): loads every non-terminal Run and publishes one
    /// <see cref="ResumeRun"/> per Run, so a single Run's resumption is a single message's concern and a
    /// crash mid-sweep re-runs cleanly. Publishing rather than resuming inline keeps the sweep itself
    /// stateless and lets the durable bus carry the fan-out. Returns the number of Runs queued to resume.
    /// </summary>
    public async Task<int> ResumeNonTerminalRunsAsync(IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bus);

        var resumable = await _runs.FindResumableRunsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var run in resumable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await bus.PublishAsync(new ResumeRun(run.Id)).ConfigureAwait(false);
        }

        _logger.LogInformation("Startup resume sweep queued {Count} non-terminal Run(s) to resume.", resumable.Count);
        return resumable.Count;
    }

    /// <summary>
    /// Re-enters a non-terminal Run at its persisted state after a restart (QG-1, AC-05). Dispatches on
    /// <see cref="Run.State"/>: a <see cref="RunState.Created"/> Run recomputes its scope (idempotently)
    /// and continues; an <see cref="RunState.Enriching"/> Run is polled, never resubmitted. Later stages
    /// (Matching onward) own their own resume in F4/F5/F8 — the orchestrator does not resubmit their work.
    /// </summary>
    public async Task Handle(ResumeRun message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("ResumeRun for unknown Run {RunId}; ignoring.", message.RunId);
            return;
        }

        if (RunTransitions.IsTerminal(run.State))
        {
            _logger.LogInformation("Run {RunId} is terminal ({State}); nothing to resume.", run.Id, run.State);
            return;
        }

        switch (run.State)
        {
            case RunState.Created:
                var scope = await SelectScopeAsync(run.CutoffFrom, run.CutoffTo, cancellationToken).ConfigureAwait(false);
                run.SetScope(scope);
                await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Resuming Run {RunId} from Created with {Scope} job(s).", run.Id, run.JobsInScope);
                await ContinueFromCreatedAsync(run, bus, cancellationToken).ConfigureAwait(false);
                break;

            case RunState.Enriching:
                _logger.LogInformation("Resuming Run {RunId} from Enriching; polling the submitted batch.", run.Id);
                await bus.PublishAsync(new BatchPollDue(run.Id)).ConfigureAwait(false);
                break;

            default:
                // Matching/Ranking/Researching/Reporting are owned by F4/F5/F8; each resumes its own stage.
                _logger.LogInformation(
                    "Run {RunId} is in downstream stage {State}; its owning feature resumes it.", run.Id, run.State);
                break;
        }
    }

    /// <summary>
    /// The shared post-scope branch for both start and resume of a <see cref="RunState.Created"/> Run:
    /// an empty scope completes the enrichment stage without submitting (Created → Enriching → Matching)
    /// and emits <see cref="EnrichmentCompleted"/> with zero counts so a digest still ships; a non-empty
    /// scope hands off to the submission step (T10), which is the only spend-committing path.
    /// </summary>
    private async Task ContinueFromCreatedAsync(Run run, IMessageBus bus, CancellationToken cancellationToken)
    {
        if (run.JobsInScope == 0)
        {
            var now = _clock.UtcNow;
            run.TransitionTo(RunState.Enriching, now);
            run.TransitionTo(RunState.Matching, now);
            await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await bus.PublishAsync(new EnrichmentCompleted(run.Id, EnrichedCount: 0, FailedCount: 0, now))
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Run {RunId} had an empty scope; enrichment completed without submitting.", run.Id);
            return;
        }

        await bus.PublishAsync(new EnrichmentSubmissionDue(run.Id)).ConfigureAwait(false);
    }

    /// <summary>
    /// The Run's scope: the live jobs first seen within its discovery window <c>[cutoff_from, cutoff_to]</c>.
    /// The upper bound is applied here so the count is fixed at Run creation and reproducible when the
    /// submission step re-reads the same window (T10) — a job discovered after <c>cutoff_to</c> belongs to
    /// the next Run, not this one.
    /// </summary>
    private async Task<int> SelectScopeAsync(
        DateTimeOffset cutoffFrom,
        DateTimeOffset cutoffTo,
        CancellationToken cancellationToken)
    {
        var discovered = await _liveJobs.DiscoveredSinceAsync(cutoffFrom, cancellationToken).ConfigureAwait(false);
        return CountInWindow(discovered, cutoffTo);
    }

    private static int CountInWindow(IReadOnlyList<LiveJob> jobs, DateTimeOffset cutoffTo)
    {
        var count = 0;
        foreach (var job in jobs)
        {
            if (job.FirstSeenAt <= cutoffTo)
            {
                count++;
            }
        }

        return count;
    }
}
