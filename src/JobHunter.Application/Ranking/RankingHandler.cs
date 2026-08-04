using JobHunter.Application.Common;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Profiles;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Ranking;

/// <summary>
/// The ranking step (F4 SAD §6.2, T08). Consumes <see cref="MatchingCompleted"/>: it loads the Run's current
/// matches (joined to their enrichments and first-seen timestamps), the active Profile and the active learned
/// preference model, scores every job with the pure <see cref="ScoreCalculator"/>, evaluates suppression with
/// the pure <see cref="SuppressionEvaluator"/>, persists a <see cref="Score"/> row per job with every component
/// recorded (QG-1, AC-03), advances the Run to <see cref="RunState.Researching"/>, and publishes
/// <see cref="RankingCompleted"/> with the counts and top job ids the digest footer needs.
///
/// <para>The ordering is arithmetic, not a model call, so it is free, instant and deterministic: re-running
/// ranking for a Run produces byte-identical scores (QG-3). Every non-suppressed job gets exactly one score
/// (AC-11), and every suppression records a reason and leaves the job retrievable — never a silent filter
/// (invariant 11, AC-05). The preference model id is stamped on each score, so a bad refit is attributable
/// (AC-04). A Run with nothing to rank — no active Profile, or an empty match set — still completes to
/// Researching and publishes a zero-count completion, because silence is worse than a reduced digest (brief §9).</para>
///
/// <para>Idempotent throughout: the score upsert is a no-op on the unique <c>(job_id, run_id)</c> key, so a
/// replay writes each score exactly once, and the advance to <c>Researching</c> is a legal-once transition that
/// a resumed pass finds already taken. No CV text is anywhere near this handler — it reads scores of a fit
/// already judged, never the CV itself (F4 invariant: the CV crosses exactly one boundary).</para>
/// </summary>
public sealed class RankingHandler(
    IRunRepository runs,
    IRankingScopeQuery scope,
    IProfileRepository profiles,
    IPreferenceModelQuery preferences,
    IScoreRepository scores,
    RankingOptions options,
    IClock clock,
    ILogger<RankingHandler> logger)
{
    private readonly IRunRepository _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IRankingScopeQuery _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly IProfileRepository _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    private readonly IPreferenceModelQuery _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    private readonly IScoreRepository _scores = scores ?? throw new ArgumentNullException(nameof(scores));
    private readonly RankingOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<RankingHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(MatchingCompleted message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var run = await _runs.FindAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("MatchingCompleted for unknown Run {RunId}; ignoring.", message.RunId);
            return;
        }

        if (RunTransitions.IsTerminal(run.State))
        {
            _logger.LogInformation("Run {RunId} is terminal ({State}); ranking already done.", run.Id, run.State);
            return;
        }

        var profile = await _profiles.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        var jobs = await _scope.InScopeAsync(run.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null || jobs.Count == 0)
        {
            // Nothing to rank: no Profile to suppress against, or no current matches. Complete to Researching
            // with a zero-count RankingCompleted so a reduced digest still ships (brief §9).
            _logger.LogInformation(
                "Run {RunId} has nothing to rank ({Reason}); completing to Researching with a zero count.",
                run.Id, profile is null ? "no active Profile" : "no current matches");
            await AdvanceAndPublishAsync(run, bus, ranked: 0, suppressed: 0, [], cancellationToken).ConfigureAwait(false);
            return;
        }

        var preference = await _preferences
            .FindActiveAsync(jobs.Select(j => j.JobId).ToList(), cancellationToken)
            .ConfigureAwait(false);

        var scored = ScoreAll(jobs, profile, preference);

        var suppressedCount = 0;
        foreach (var item in scored)
        {
            if (item.Suppressed)
            {
                suppressedCount++;
                Telemetry.RankingSuppressed.Add(1);
            }

            Telemetry.MatchScoreDistribution.Record((double)item.Result.FinalScore);

            var score = new Score(
                item.Result.JobId, run.Id, item.Result.FinalScore, item.Result.Components,
                item.Result.EffectiveWeights, preference?.ModelId, item.Suppressed, item.Reason, _clock.UtcNow);
            await _scores.UpsertAsync(score, cancellationToken).ConfigureAwait(false);
        }

        var rankedCount = scored.Count - suppressedCount;
        var topJobIds = ScoreCalculator
            .Rank(scored.Where(s => !s.Suppressed).Select(s => s.Result))
            .Take(_options.TopJobCount)
            .Select(r => r.JobId)
            .ToList();

        await AdvanceAndPublishAsync(run, bus, rankedCount, suppressedCount, topJobIds, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Ranked Run {RunId}: {Ranked} shown, {Suppressed} suppressed.", run.Id, rankedCount, suppressedCount);
    }

    private List<ScoredJob> ScoreAll(IReadOnlyList<RankingJob> jobs, Profile profile, ActivePreference? preference)
    {
        var results = new List<ScoredJob>(jobs.Count);
        foreach (var job in jobs)
        {
            decimal? preferenceComponent = preference is not null
                && preference.ComponentByJob.TryGetValue(job.JobId, out var component)
                ? component
                : null;

            var result = ScoreCalculator.Calculate(
                new MatchFacts(job.JobId, job.MatchScore),
                preferenceComponent,
                job.HasEnrichment,
                job.FirstSeenAt,
                _clock.UtcNow,
                RankingWeights.Default);

            // Suppression is evaluated once, here, and the reason carried alongside the result, so the
            // persisted flag and the top-jobs projection never disagree.
            var reason = SuppressionEvaluator.Evaluate(
                result, job.EstimatedSalary, profile, _options.SalaryFloorSuppression);
            results.Add(new ScoredJob(result, reason));
        }

        return results;
    }

    private async Task AdvanceAndPublishAsync(
        Run run, IMessageBus bus, int ranked, int suppressed, IReadOnlyList<Guid> topJobIds,
        CancellationToken cancellationToken)
    {
        var advanced = run.TransitionTo(RunState.Researching, _clock.UtcNow);
        await _runs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (advanced.IsSuccess)
        {
            await bus.PublishAsync(new RankingCompleted(run.Id, ranked, suppressed, topJobIds, _clock.UtcNow))
                .ConfigureAwait(false);
        }
    }

    /// <summary>A scored job with its suppression verdict, so the reason is computed once and reused.</summary>
    private readonly record struct ScoredJob(ScoreResult Result, string? Reason)
    {
        public bool Suppressed => Reason is not null;
    }
}
