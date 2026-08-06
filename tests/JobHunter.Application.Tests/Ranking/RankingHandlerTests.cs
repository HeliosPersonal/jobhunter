using JobHunter.Application.Ranking;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Profiles;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Ranking;

/// <summary>
/// T08: the ranking step (F4 SAD §6.2). Consumes <see cref="MatchingCompleted"/>, scores every current match with
/// the pure <see cref="ScoreCalculator"/>, evaluates suppression, persists a <see cref="Score"/> per job, advances
/// the Run to Researching and publishes <see cref="RankingCompleted"/>. The properties that carry the feature:
/// every non-suppressed job gets <em>exactly one</em> score (AC-11); every suppression <em>records a reason</em>
/// and leaves the job retrievable (AC-05, invariant 11); the preference model id is <em>stamped</em> on each score
/// (AC-04); a Run with nothing to rank <em>still completes</em> to Researching (brief §9); and re-running ranking
/// writes each score <em>exactly once</em> and produces identical totals (QG-3, idempotency). Every collaborator is
/// substituted, so these are zero-database unit tests.
/// </summary>
public sealed class RankingHandlerTests
{
    private static readonly DateTimeOffset RunStart = new(2026, 8, 4, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 5, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IRankingScopeQuery _scope = Substitute.For<IRankingScopeQuery>();
    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly IPreferenceModelQuery _preferences = Substitute.For<IPreferenceModelQuery>();
    private readonly ISuppressionOverrideQuery _overrides = Substitute.For<ISuppressionOverrideQuery>();
    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly FakeScoreRepository _scores = new();
    private readonly FakeClock _clock = new(Now);
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    public RankingHandlerTests()
    {
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(ActiveProfile());
        _preferences.FindActiveAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((ActivePreference?)null);
        _overrides.AllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SuppressionOverride>)[]);
    }

    private void GivenOverrides(params SuppressionOverride[] rules) =>
        _overrides.AllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SuppressionOverride>)rules);

    private void GivenFacts(Guid jobId, Dimension dimension, params string[] values) =>
        _facts.SnapshotAsync(jobId, Arg.Any<CancellationToken>())
            .Returns(JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>> { [dimension] = values }));

    private static Profile ActiveProfile(bool salaryFloor = false) =>
        new(ProfileId, isActive: true, "Owner", salaryFloor ? 120000m : null, salaryFloor ? "USD" : null,
            TimezoneBand.EMEA, ["Portugal"], [EmploymentType.FullTime], RunStart);

    private RankingHandler CreateHandler(RankingOptions? options = null) =>
        new(_runs, _scope, _profiles, _preferences, _overrides, _facts, _scores, options ?? new RankingOptions(),
            _clock, NullLogger<RankingHandler>.Instance);

    private static Run RankingRun()
    {
        var run = new Run(RunId, RunStart.AddHours(-24), RunStart, 2.00m, RunStart.AddMinutes(-5));
        run.SetScope(3);
        run.TransitionTo(RunState.Enriching, RunStart);
        run.TransitionTo(RunState.Matching, RunStart);
        run.TransitionTo(RunState.Ranking, RunStart);
        return run;
    }

    private void GivenRun(Run run) =>
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);

    private void GivenJobs(params RankingJob[] jobs) =>
        _scope.InScopeAsync(RunId, Arg.Any<CancellationToken>()).Returns(jobs);

    private static RankingJob Job(Guid id, int matchScore, bool enriched = true, DateTimeOffset? firstSeen = null,
        SalaryEstimate? estimate = null,
        AiUsageLevel aiUsage = AiUsageLevel.None, RoleFamily roleFamily = RoleFamily.Other) =>
        new(id, matchScore, firstSeen ?? Now, enriched, estimate, aiUsage, roleFamily);

    private static SalaryEstimate Estimate(decimal min, decimal max, string currency, decimal confidence) =>
        SalaryEstimate.TryCreate(min, max, currency, SalaryPeriod.Year, confidence).Value;

    private List<object> Publishes() =>
        _bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMessageBus.PublishAsync))
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 0 && a[0] is not null)
            .Select(a => a[0]!)
            .ToList();

    private static MatchingCompleted Message() => new(RunId, Succeeded: 3, Failed: 0, CostUsd: 0.44m, Now);

    // ---- AC-11: every job gets exactly one score, and the Run advances ------------------------

    [Fact]
    public async Task Every_matched_job_gets_exactly_one_score_and_the_run_advances_to_researching()
    {
        var run = RankingRun();
        GivenRun(run);
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenJobs(Job(jobs[0], 90), Job(jobs[1], 80), Job(jobs[2], 70));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.Count.ShouldBe(3);
        _scores.Stored.Select(s => s.JobId).ShouldBe(jobs, ignoreOrder: true);
        run.State.ShouldBe(RunState.Researching);

        var completed = Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem();
        completed.RankedCount.ShouldBe(3);
        completed.SuppressedCount.ShouldBe(0);
        completed.TopJobIds.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Top_job_ids_are_ordered_by_descending_final_score()
    {
        var run = RankingRun();
        GivenRun(run);
        var high = Guid.CreateVersion7();
        var mid = Guid.CreateVersion7();
        var low = Guid.CreateVersion7();
        GivenJobs(Job(low, 60), Job(high, 95), Job(mid, 80));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var completed = Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem();
        completed.TopJobIds[0].ShouldBe(high);
        completed.TopJobIds[1].ShouldBe(mid);
        completed.TopJobIds[2].ShouldBe(low);
    }

    [Fact]
    public async Task The_top_ids_are_capped_at_the_configured_count()
    {
        var run = RankingRun();
        GivenRun(run);
        var jobs = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        GivenJobs(jobs.Select((id, i) => Job(id, 90 - i)).ToArray());

        await CreateHandler(new RankingOptions { TopJobCount = 2 }).Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.Count.ShouldBe(5);
        Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem().TopJobIds.Count.ShouldBe(2);
    }

    // ---- AC-05 / invariant 11: a suppressed job keeps a score row and a reason, and stays out of top -----

    [Fact]
    public async Task A_job_below_the_threshold_is_scored_suppressed_with_a_reason_and_excluded_from_top()
    {
        var run = RankingRun();
        GivenRun(run);
        var shown = Guid.CreateVersion7();
        var hidden = Guid.CreateVersion7();
        // A very low match with no enrichment (0.85 confidence) lands below the 40 presentation threshold.
        GivenJobs(Job(shown, 90), Job(hidden, 5, enriched: false));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var hiddenScore = _scores.Stored.Single(s => s.JobId == hidden);
        hiddenScore.Suppressed.ShouldBeTrue();
        hiddenScore.SuppressionReason.ShouldBe("Below presentation threshold");

        var completed = Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem();
        completed.RankedCount.ShouldBe(1);
        completed.SuppressedCount.ShouldBe(1);
        completed.TopJobIds.ShouldBe([shown]);
    }

    [Fact]
    public async Task All_jobs_suppressed_still_advances_and_reports_with_an_empty_top_set()
    {
        var run = RankingRun();
        GivenRun(run);
        GivenJobs(Job(Guid.CreateVersion7(), 3, enriched: false), Job(Guid.CreateVersion7(), 2, enriched: false));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.Count.ShouldBe(2);
        _scores.Stored.ShouldAllBe(s => s.Suppressed);
        run.State.ShouldBe(RunState.Researching);

        var completed = Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem();
        completed.RankedCount.ShouldBe(0);
        completed.SuppressedCount.ShouldBe(2);
        completed.TopJobIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_opted_in_salary_floor_suppresses_a_high_confidence_low_paying_job()
    {
        var run = RankingRun();
        GivenRun(run);
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns(ActiveProfile(salaryFloor: true));
        var lowPay = Guid.CreateVersion7();
        GivenJobs(Job(lowPay, 90, estimate: Estimate(40000m, 60000m, "USD", 0.95m)));

        await CreateHandler(new RankingOptions { SalaryFloorSuppression = true })
            .Handle(Message(), _bus, CancellationToken.None);

        var score = _scores.Stored.Single();
        score.Suppressed.ShouldBeTrue();
        score.SuppressionReason.ShouldBe("Below salary floor (USD 120000)");
    }

    // ---- AC-04: the preference model id is stamped on every score -----------------------------

    [Fact]
    public async Task The_active_preference_model_id_is_stamped_on_every_score()
    {
        var run = RankingRun();
        GivenRun(run);
        var modelId = Guid.Parse("00000000-0000-0000-0000-0000000000E1");
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenJobs(Job(jobs[0], 90), Job(jobs[1], 80));
        _preferences.FindActiveAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new ActivePreference(modelId, new Dictionary<Guid, decimal>
            {
                [jobs[0]] = 0.9m,
                [jobs[1]] = 0.4m,
            }));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.ShouldAllBe(s => s.PreferenceModelId == modelId);
        // With a preference present the component is stored, not renormalised away.
        _scores.Stored.Single(s => s.JobId == jobs[0]).Components.Preference.ShouldBe(0.9m);
    }

    [Fact]
    public async Task With_no_active_preference_model_the_score_carries_no_model_id()
    {
        var run = RankingRun();
        GivenRun(run);
        GivenJobs(Job(Guid.CreateVersion7(), 90));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.Single().PreferenceModelId.ShouldBeNull();
    }

    // ---- QG-3 / idempotency: re-running writes each score once and keeps identical totals ------

    [Fact]
    public async Task Re_running_ranking_writes_each_score_exactly_once_with_identical_totals()
    {
        var run = RankingRun();
        var jobs = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        GivenJobs(Job(jobs[0], 90), Job(jobs[1], 70));

        // First pass advances the Run to Researching; the second pass must find it already advanced and write
        // no second score row (the unique (job_id, run_id) key makes the upsert a no-op).
        GivenRun(run);
        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);
        var firstTotals = _scores.Stored.ToDictionary(s => s.JobId, s => s.FinalScore);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.Count.ShouldBe(2);
        _scores.WriteAttempts.ShouldBe(4); // two per pass, the second pass all no-ops
        _scores.Stored.ToDictionary(s => s.JobId, s => s.FinalScore).ShouldBe(firstTotals);
    }

    // ---- nothing to rank still completes -------------------------------------------------------

    [Fact]
    public async Task A_run_with_no_current_matches_completes_to_researching_with_a_zero_count()
    {
        var run = RankingRun();
        GivenRun(run);
        GivenJobs();

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.ShouldBeEmpty();
        run.State.ShouldBe(RunState.Researching);
        var completed = Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem();
        completed.RankedCount.ShouldBe(0);
        completed.SuppressedCount.ShouldBe(0);
        completed.TopJobIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_with_no_active_profile_completes_to_researching_without_scoring()
    {
        var run = RankingRun();
        GivenRun(run);
        _profiles.FindActiveAsync(Arg.Any<CancellationToken>()).Returns((Profile?)null);
        GivenJobs(Job(Guid.CreateVersion7(), 90));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.ShouldBeEmpty();
        run.State.ShouldBe(RunState.Researching);
        Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem().RankedCount.ShouldBe(0);
    }

    // ---- T07 / AC-06: Owner overrides outrank the learner --------------------------------------

    [Fact]
    public async Task A_never_suppress_override_forces_a_below_threshold_job_to_appear()
    {
        var run = RankingRun();
        GivenRun(run);
        var forced = Guid.CreateVersion7();
        // The job would fall below the presentation threshold, but the Owner said Germany must always show.
        GivenJobs(Job(forced, 5, enriched: false));
        GivenOverrides(new SuppressionOverride(
            Guid.CreateVersion7(), Dimension.Country, "DE", SuppressionMode.NeverSuppress, Now));
        GivenFacts(forced, Dimension.Country, "DE");

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var score = _scores.Stored.Single();
        score.Suppressed.ShouldBeFalse();
        score.SuppressionReason.ShouldBeNull();
        Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem().SuppressedCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_always_suppress_override_hides_a_shown_job_with_the_override_reason()
    {
        var run = RankingRun();
        GivenRun(run);
        var hidden = Guid.CreateVersion7();
        // A strong match the model would show, but the Owner said this role family must always hide.
        GivenJobs(Job(hidden, 90));
        GivenOverrides(new SuppressionOverride(
            Guid.CreateVersion7(), Dimension.RoleFamily, "Other", SuppressionMode.AlwaysSuppress, Now));
        GivenFacts(hidden, Dimension.RoleFamily, "Other");

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var score = _scores.Stored.Single();
        score.Suppressed.ShouldBeTrue();
        score.SuppressionReason.ShouldNotBeNull().ShouldContain("Other");
        Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem().SuppressedCount.ShouldBe(1);
    }

    [Fact]
    public async Task With_no_overrides_the_facts_snapshot_is_never_consulted()
    {
        var run = RankingRun();
        GivenRun(run);
        GivenJobs(Job(Guid.CreateVersion7(), 90));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        // The common day: no override rule means no per-job facts lookup at all.
        await _facts.DidNotReceive().SnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ---- guards --------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_is_ignored()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns((Run?)null);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_terminal_run_is_ignored()
    {
        var run = RankingRun();
        run.Abort("done", Now, costBreach: false);
        GivenRun(run);

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.ShouldBeEmpty();
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_matched_but_unenriched_job_is_still_scored_at_a_discounted_confidence()
    {
        var run = RankingRun();
        GivenRun(run);
        var id = Guid.CreateVersion7();
        GivenJobs(Job(id, 90, enriched: false));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.Single().Components.ConfidenceMultiplier.ShouldBe(0.85m);
    }

    // ---- T14: the alignment component flows from the enrichment signals into the stored score --------

    [Fact]
    public async Task A_tier_one_ai_role_outranks_an_anti_goal_role_of_the_same_fit()
    {
        var run = RankingRun();
        GivenRun(run);
        var aligned = Guid.CreateVersion7();
        var antiGoal = Guid.CreateVersion7();
        // Identical fit, freshness and confidence: the only difference is career alignment.
        GivenJobs(
            Job(aligned, 80, aiUsage: AiUsageLevel.High, roleFamily: RoleFamily.AiPlatform),
            Job(antiGoal, 80, aiUsage: AiUsageLevel.None, roleFamily: RoleFamily.EnterpriseCrud));

        await CreateHandler().Handle(Message(), _bus, CancellationToken.None);

        var alignedScore = _scores.Stored.Single(s => s.JobId == aligned);
        var antiGoalScore = _scores.Stored.Single(s => s.JobId == antiGoal);

        alignedScore.Components.Alignment.ShouldBe(1.0m);
        antiGoalScore.Components.Alignment.ShouldBe(0.0m);
        alignedScore.FinalScore.ShouldBeGreaterThan(antiGoalScore.FinalScore);

        var completed = Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem();
        completed.TopJobIds[0].ShouldBe(aligned);
    }

    // ---- T15: the anti-goal down-weight and its opt-in suppression -----------------------------

    [Fact]
    public async Task An_anti_goal_role_is_down_weighted_by_the_configured_penalty_factor()
    {
        var run = RankingRun();
        GivenRun(run);
        var antiGoal = Guid.CreateVersion7();
        // A perfect-fit anti-goal role: low AI on the enterprise-CRUD family. A mild penalty (0.9) isolates the
        // stored down-weight from the presentation threshold, so we can see the multiplier without it also being
        // suppressed for being below 40 — that interaction is asserted separately below.
        GivenJobs(Job(antiGoal, 100, aiUsage: AiUsageLevel.None, roleFamily: RoleFamily.EnterpriseCrud));

        await CreateHandler(new RankingOptions { AntiGoalPenaltyFactor = 0.9m })
            .Handle(Message(), _bus, CancellationToken.None);

        var score = _scores.Stored.Single(s => s.JobId == antiGoal);
        // The multiplier is stored (reconcilable, QG-1), not a silent adjustment; the job stays visible.
        score.Components.AntiGoalMultiplier.ShouldBe(0.9m);
        score.Suppressed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_default_penalty_pushes_a_perfect_fit_anti_goal_role_below_the_presentation_threshold()
    {
        var run = RankingRun();
        GivenRun(run);
        var antiGoal = Guid.CreateVersion7();
        // With alignment pinned at 0 for the enterprise-CRUD family, even a perfect-fit anti-goal role tops out
        // at 100 × 0.75 × 0.5 = 37.5 under the default penalty — below the 40 threshold — so the down-weight
        // alone drives a reasoned suppression, not a silent filter (invariant 11). The job stays retrievable.
        GivenJobs(Job(antiGoal, 100, aiUsage: AiUsageLevel.None, roleFamily: RoleFamily.EnterpriseCrud));

        await CreateHandler(new RankingOptions()).Handle(Message(), _bus, CancellationToken.None);

        var score = _scores.Stored.Single(s => s.JobId == antiGoal);
        score.Components.AntiGoalMultiplier.ShouldBe(0.5m);
        score.Suppressed.ShouldBeTrue();
        score.SuppressionReason.ShouldBe("Below presentation threshold");
    }

    [Fact]
    public async Task A_non_anti_goal_role_carries_a_neutral_anti_goal_multiplier()
    {
        var run = RankingRun();
        GivenRun(run);
        var ordinary = Guid.CreateVersion7();
        GivenJobs(Job(ordinary, 80, aiUsage: AiUsageLevel.High, roleFamily: RoleFamily.AiPlatform));

        await CreateHandler(new RankingOptions { AntiGoalPenaltyFactor = 0.5m })
            .Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.Single().Components.AntiGoalMultiplier.ShouldBe(1.00m);
    }

    [Fact]
    public async Task A_high_fit_anti_goal_role_no_longer_out_ranks_a_tier_one_alignment_role()
    {
        var run = RankingRun();
        GivenRun(run);
        var aligned = Guid.CreateVersion7();
        var antiGoal = Guid.CreateVersion7();
        // The anti-goal role has the higher raw fit (100 vs 70); the down-weight must stop it out-ranking the
        // genuinely aligned Tier-1 role (feeds T19). Without the penalty the CRUD role would float to the top.
        GivenJobs(
            Job(aligned, 70, aiUsage: AiUsageLevel.High, roleFamily: RoleFamily.AiPlatform),
            Job(antiGoal, 100, aiUsage: AiUsageLevel.None, roleFamily: RoleFamily.EnterpriseCrud));

        await CreateHandler(new RankingOptions { AntiGoalPenaltyFactor = 0.5m })
            .Handle(Message(), _bus, CancellationToken.None);

        var alignedScore = _scores.Stored.Single(s => s.JobId == aligned);
        var antiGoalScore = _scores.Stored.Single(s => s.JobId == antiGoal);
        alignedScore.FinalScore.ShouldBeGreaterThan(antiGoalScore.FinalScore);

        Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem().TopJobIds[0].ShouldBe(aligned);
    }

    [Fact]
    public async Task An_opted_in_anti_goal_role_is_suppressed_with_the_family_reason()
    {
        var run = RankingRun();
        GivenRun(run);
        var antiGoal = Guid.CreateVersion7();
        GivenJobs(Job(antiGoal, 100, aiUsage: AiUsageLevel.None, roleFamily: RoleFamily.EnterpriseCrud));

        await CreateHandler(new RankingOptions { AntiGoalSuppression = true })
            .Handle(Message(), _bus, CancellationToken.None);

        var score = _scores.Stored.Single();
        // A suppressed anti-goal job stays retrievable and carries the specific reason (invariant 11).
        score.Suppressed.ShouldBeTrue();
        score.SuppressionReason.ShouldBe("Anti-goal role family: EnterpriseCrud");
        Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem().SuppressedCount.ShouldBe(1);
    }

    // ---- T17: the negative role-family down-weight and its opt-in suppression ------------------

    [Fact]
    public async Task A_negative_family_role_is_down_weighted_by_the_configured_penalty_factor()
    {
        var run = RankingRun();
        GivenRun(run);
        var research = Guid.CreateVersion7();
        // A high-fit ML-research role: off-target by default. A mild penalty (0.9) isolates the stored
        // down-weight from the presentation threshold, so we can see the multiplier without it also being
        // suppressed for being below 40.
        GivenJobs(Job(research, 100, aiUsage: AiUsageLevel.High, roleFamily: RoleFamily.MlResearch));

        await CreateHandler(new RankingOptions { NegativeFamilyPenaltyFactor = 0.9m })
            .Handle(Message(), _bus, CancellationToken.None);

        var score = _scores.Stored.Single(s => s.JobId == research);
        // The penalty is folded into the stored career-policy multiplier (reconcilable, QG-1); the job stays visible.
        score.Components.AntiGoalMultiplier.ShouldBe(0.9m);
        score.Suppressed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_target_family_role_carries_a_neutral_career_policy_multiplier()
    {
        var run = RankingRun();
        GivenRun(run);
        var ordinary = Guid.CreateVersion7();
        GivenJobs(Job(ordinary, 80, aiUsage: AiUsageLevel.High, roleFamily: RoleFamily.AiPlatform));

        await CreateHandler(new RankingOptions { NegativeFamilyPenaltyFactor = 0.5m })
            .Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.Single().Components.AntiGoalMultiplier.ShouldBe(1.00m);
    }

    [Fact]
    public async Task A_high_fit_negative_family_role_no_longer_out_ranks_a_tier_one_alignment_role()
    {
        var run = RankingRun();
        GivenRun(run);
        var aligned = Guid.CreateVersion7();
        var research = Guid.CreateVersion7();
        // The research role has the higher raw fit (100 vs 70) and high AI usage; the down-weight must stop it
        // out-ranking the genuinely aligned Tier-1 role (feeds T19). A mild 0.6 penalty keeps both visible so the
        // relative order is the property under test, not suppression.
        GivenJobs(
            Job(aligned, 70, aiUsage: AiUsageLevel.High, roleFamily: RoleFamily.AiPlatform),
            Job(research, 100, aiUsage: AiUsageLevel.High, roleFamily: RoleFamily.MlResearch));

        await CreateHandler(new RankingOptions { NegativeFamilyPenaltyFactor = 0.6m })
            .Handle(Message(), _bus, CancellationToken.None);

        var alignedScore = _scores.Stored.Single(s => s.JobId == aligned);
        var researchScore = _scores.Stored.Single(s => s.JobId == research);
        alignedScore.FinalScore.ShouldBeGreaterThan(researchScore.FinalScore);

        Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem().TopJobIds[0].ShouldBe(aligned);
    }

    [Fact]
    public async Task An_opted_in_negative_family_role_is_suppressed_with_the_family_reason()
    {
        var run = RankingRun();
        GivenRun(run);
        var research = Guid.CreateVersion7();
        GivenJobs(Job(research, 100, aiUsage: AiUsageLevel.High, roleFamily: RoleFamily.MlResearch));

        await CreateHandler(new RankingOptions { NegativeFamilySuppression = true })
            .Handle(Message(), _bus, CancellationToken.None);

        var score = _scores.Stored.Single();
        // A suppressed off-target job stays retrievable and carries the specific reason (invariant 11).
        score.Suppressed.ShouldBeTrue();
        score.SuppressionReason.ShouldBe("Not a target role family: MlResearch");
        Publishes().OfType<RankingCompleted>().ShouldHaveSingleItem().SuppressedCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_negative_family_role_is_not_penalised_when_the_configured_set_is_empty()
    {
        var run = RankingRun();
        GivenRun(run);
        var research = Guid.CreateVersion7();
        // The Owner has turned the filter off entirely: an ML-research role rides its fit at a neutral multiplier.
        GivenJobs(Job(research, 90, aiUsage: AiUsageLevel.High, roleFamily: RoleFamily.MlResearch));

        await CreateHandler(new RankingOptions { NegativeRoleFamilies = new HashSet<RoleFamily>() })
            .Handle(Message(), _bus, CancellationToken.None);

        _scores.Stored.Single().Components.AntiGoalMultiplier.ShouldBe(1.00m);
    }

    /// <summary>Models the idempotent upsert on the unique <c>(job_id, run_id)</c> key of the scores table.</summary>
    private sealed class FakeScoreRepository : IScoreRepository
    {
        public List<Score> Stored { get; } = [];

        public int WriteAttempts { get; private set; }

        public Task<bool> UpsertAsync(Score score, CancellationToken cancellationToken = default)
        {
            WriteAttempts++;
            var isNew = !Stored.Any(s => s.JobId == score.JobId && s.RunId == score.RunId);
            if (isNew)
            {
                Stored.Add(score);
            }

            return Task.FromResult(isNew);
        }

        public Task<Score?> FindAsync(Guid jobId, Guid runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.FirstOrDefault(s => s.JobId == jobId && s.RunId == runId));
    }
}
