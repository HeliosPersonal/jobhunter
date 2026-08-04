using JobHunter.Application.Ranking;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ranking;

/// <summary>
/// T08: the post-ranking suppression rules (match-schema §Suppression, invariant 11). A suppressed job is never a
/// silent filter — it always carries a reason so the digest footer can say what was withheld and why (AC-05).
/// These are pure, zero-dependency assertions over <see cref="SuppressionEvaluator"/>: the presentation threshold
/// bites below 40; the salary floor is off by default (a down-weight, not a filter — O5) and, when opted in, only
/// bites at high confidence, in the same currency, when the whole band misses the floor.
/// </summary>
public sealed class SuppressionEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 5, 0, 0, TimeSpan.Zero);

    private static Profile ProfileWithFloor(decimal? floor = null, string? currency = null) =>
        new(Guid.Parse("00000000-0000-0000-0000-0000000000A1"), isActive: true, "Owner",
            floor, currency, TimezoneBand.EMEA, ["Portugal"], [EmploymentType.FullTime], Now);

    private static ScoreResult ScoreOf(decimal finalScore)
    {
        // A score whose components reconcile to finalScore is not needed here — the evaluator reads FinalScore
        // only — so build a minimal, in-range ScoreResult carrying the target total.
        var components = new ScoreComponents(0.5m, 0m, 0m, 0.5m, 1.00m);
        return new ScoreResult(Guid.Parse("00000000-0000-0000-0000-0000000000F1"), finalScore, components,
            RankingWeights.Default, PreferencePresent: false);
    }

    private static SalaryEstimate Estimate(decimal min, decimal max, string currency, decimal confidence) =>
        SalaryEstimate.TryCreate(min, max, currency, SalaryPeriod.Year, confidence).Value;

    [Theory]
    [InlineData(39.99)]
    [InlineData(0)]
    [InlineData(10)]
    public void A_score_below_the_presentation_threshold_is_suppressed_with_a_reason(decimal finalScore)
    {
        var reason = SuppressionEvaluator.Evaluate(ScoreOf(finalScore), null, ProfileWithFloor(), salaryFloorOptIn: false);

        reason.ShouldBe("Below presentation threshold");
    }

    [Theory]
    [InlineData(40)]
    [InlineData(40.01)]
    [InlineData(100)]
    public void A_score_at_or_above_the_threshold_is_shown(decimal finalScore)
    {
        var reason = SuppressionEvaluator.Evaluate(ScoreOf(finalScore), null, ProfileWithFloor(), salaryFloorOptIn: false);

        reason.ShouldBeNull();
    }

    [Fact]
    public void The_salary_floor_is_off_by_default_even_when_the_estimate_is_well_below_it()
    {
        var profile = ProfileWithFloor(120000m, "USD");
        var estimate = Estimate(40000m, 60000m, "USD", 0.95m);

        var reason = SuppressionEvaluator.Evaluate(ScoreOf(80m), estimate, profile, salaryFloorOptIn: false);

        reason.ShouldBeNull();
    }

    [Fact]
    public void An_opted_in_high_confidence_estimate_whose_whole_band_misses_the_floor_is_suppressed()
    {
        var profile = ProfileWithFloor(120000m, "USD");
        var estimate = Estimate(40000m, 60000m, "USD", 0.9m);

        var reason = SuppressionEvaluator.Evaluate(ScoreOf(80m), estimate, profile, salaryFloorOptIn: true);

        reason.ShouldBe("Below salary floor (USD 120000)");
    }

    [Fact]
    public void A_band_whose_top_reaches_the_floor_is_not_suppressed()
    {
        var profile = ProfileWithFloor(120000m, "USD");
        // Max meets the floor: the role could plausibly pay it, so the floor does not bite.
        var estimate = Estimate(90000m, 120000m, "USD", 0.95m);

        var reason = SuppressionEvaluator.Evaluate(ScoreOf(80m), estimate, profile, salaryFloorOptIn: true);

        reason.ShouldBeNull();
    }

    [Fact]
    public void A_low_confidence_estimate_below_the_floor_is_not_suppressed_even_when_opted_in()
    {
        var profile = ProfileWithFloor(120000m, "USD");
        var estimate = Estimate(40000m, 60000m, "USD", 0.5m);

        var reason = SuppressionEvaluator.Evaluate(ScoreOf(80m), estimate, profile, salaryFloorOptIn: true);

        reason.ShouldBeNull();
    }

    [Fact]
    public void A_floor_is_never_compared_across_currencies()
    {
        var profile = ProfileWithFloor(120000m, "USD");
        // A euros estimate below a dollars floor is not a comparison we are willing to make (SalaryEstimate).
        var estimate = Estimate(40000m, 60000m, "EUR", 0.95m);

        var reason = SuppressionEvaluator.Evaluate(ScoreOf(80m), estimate, profile, salaryFloorOptIn: true);

        reason.ShouldBeNull();
    }

    [Fact]
    public void No_floor_on_the_profile_means_no_floor_suppression()
    {
        var profile = ProfileWithFloor();
        var estimate = Estimate(40000m, 60000m, "USD", 0.95m);

        var reason = SuppressionEvaluator.Evaluate(ScoreOf(80m), estimate, profile, salaryFloorOptIn: true);

        reason.ShouldBeNull();
    }

    [Fact]
    public void A_missing_estimate_cannot_trip_the_floor()
    {
        var profile = ProfileWithFloor(120000m, "USD");

        var reason = SuppressionEvaluator.Evaluate(ScoreOf(80m), estimatedSalary: null, profile, salaryFloorOptIn: true);

        reason.ShouldBeNull();
    }

    [Fact]
    public void The_threshold_rule_takes_precedence_over_the_floor_rule()
    {
        var profile = ProfileWithFloor(120000m, "USD");
        var estimate = Estimate(40000m, 60000m, "USD", 0.95m);

        // Below the threshold and below the floor: the threshold reason is the one reported.
        var reason = SuppressionEvaluator.Evaluate(ScoreOf(20m), estimate, profile, salaryFloorOptIn: true);

        reason.ShouldBe("Below presentation threshold");
    }

    [Fact]
    public void A_null_profile_is_a_programmer_error()
    {
        Should.Throw<ArgumentNullException>(() =>
            SuppressionEvaluator.Evaluate(ScoreOf(80m), null, null!, salaryFloorOptIn: false));
    }

    [Fact]
    public void An_anti_goal_role_is_not_suppressed_by_default()
    {
        // T15: the anti-goal down-weight is a penalty, not a filter, until explicitly opted in. A verdict
        // alone must never hide the job — it stays visible (merely down-weighted) so it is not a silent filter.
        var antiGoal = new AntiGoalVerdict(true, "Anti-goal role family: EnterpriseCrud");

        var reason = SuppressionEvaluator.Evaluate(
            ScoreOf(80m), null, ProfileWithFloor(), salaryFloorOptIn: false,
            antiGoal, antiGoalSuppressionOptIn: false);

        reason.ShouldBeNull();
    }

    [Fact]
    public void An_opted_in_anti_goal_role_is_suppressed_with_the_verdict_reason()
    {
        var antiGoal = new AntiGoalVerdict(true, "Anti-goal role family: EnterpriseCrud");

        var reason = SuppressionEvaluator.Evaluate(
            ScoreOf(80m), null, ProfileWithFloor(), salaryFloorOptIn: false,
            antiGoal, antiGoalSuppressionOptIn: true);

        reason.ShouldBe("Anti-goal role family: EnterpriseCrud");
    }

    [Fact]
    public void A_non_anti_goal_role_is_unaffected_by_the_opt_in()
    {
        var reason = SuppressionEvaluator.Evaluate(
            ScoreOf(80m), null, ProfileWithFloor(), salaryFloorOptIn: false,
            AntiGoalVerdict.None, antiGoalSuppressionOptIn: true);

        reason.ShouldBeNull();
    }

    [Fact]
    public void The_anti_goal_reason_takes_precedence_over_the_threshold_reason()
    {
        // An opted-in anti-goal role also below the threshold reports the specific anti-goal reason — the
        // deliberate policy the Owner set up is more informative than the generic threshold.
        var antiGoal = new AntiGoalVerdict(true, "Anti-goal role family: EnterpriseCrud");

        var reason = SuppressionEvaluator.Evaluate(
            ScoreOf(20m), null, ProfileWithFloor(), salaryFloorOptIn: false,
            antiGoal, antiGoalSuppressionOptIn: true);

        reason.ShouldBe("Anti-goal role family: EnterpriseCrud");
    }
}
