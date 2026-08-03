using JobHunter.Domain.Intelligence;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Intelligence;

public sealed class ScoreTests
{
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid PreferenceModelId = Guid.Parse("00000000-0000-0000-0000-0000000000E1");

    private static Score NewScore(
        ScoreComponents? components = null,
        RankingWeights? weights = null,
        decimal? finalScore = null,
        bool suppressed = false,
        string? suppressionReason = null,
        Guid? preferenceModelId = null)
    {
        var clock = new FakeClock();
        var w = weights ?? RankingWeights.Default;
        var c = components ?? new ScoreComponents(0.80m, 0.50m, 0.40m, 1.00m);
        var total = finalScore ?? c.Reconcile(w);
        return new Score(
            JobId,
            RunId,
            total,
            c,
            w,
            preferenceModelId,
            suppressed,
            suppressionReason,
            clock.UtcNow);
    }

    [Fact]
    public void A_valid_score_reconciles_and_exposes_its_fields()
    {
        var components = new ScoreComponents(0.80m, 0.50m, 0.40m, 1.00m);
        var expected = components.Reconcile(RankingWeights.Default);

        var score = NewScore(components: components, preferenceModelId: PreferenceModelId);

        score.JobId.ShouldBe(JobId);
        score.RunId.ShouldBe(RunId);
        score.FinalScore.ShouldBe(expected);
        score.Components.ShouldBe(components);
        score.PreferenceModelId.ShouldBe(PreferenceModelId);
        score.Suppressed.ShouldBeFalse();
        score.SuppressionReason.ShouldBeNull();
    }

    [Fact]
    public void A_total_that_does_not_reconcile_to_its_components_is_rejected()
    {
        // AC-03 / QG-1: a score that cannot be rebuilt from its components and weights cannot exist.
        Should.Throw<ArgumentException>(() => NewScore(finalScore: 99m));
    }

    [Fact]
    public void A_total_within_rounding_tolerance_reconciles()
    {
        var components = new ScoreComponents(0.80m, 0.50m, 0.40m, 1.00m);
        var exact = components.Reconcile(RankingWeights.Default);

        // A hundredth of a point of drift is a rounding difference, not a bug.
        var score = NewScore(components: components, finalScore: exact + 0.005m);

        score.ShouldNotBeNull();
    }

    [Fact]
    public void A_suppressed_score_without_a_reason_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewScore(suppressed: true, suppressionReason: null));
    }

    [Fact]
    public void A_suppressed_score_carries_its_trimmed_reason()
    {
        var score = NewScore(suppressed: true, suppressionReason: "  Below presentation threshold  ");

        score.Suppressed.ShouldBeTrue();
        score.SuppressionReason.ShouldBe("Below presentation threshold");
    }

    [Fact]
    public void A_non_suppressed_score_with_a_reason_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewScore(suppressed: false, suppressionReason: "leftover"));
    }

    [Fact]
    public void An_empty_job_or_run_id_is_rejected()
    {
        var clock = new FakeClock();
        var c = new ScoreComponents(0.80m, 0.50m, 0.40m, 1.00m);
        var total = c.Reconcile(RankingWeights.Default);

        Should.Throw<ArgumentException>(() => new Score(
            Guid.Empty, RunId, total, c, RankingWeights.Default, null, false, null, clock.UtcNow));
        Should.Throw<ArgumentException>(() => new Score(
            JobId, Guid.Empty, total, c, RankingWeights.Default, null, false, null, clock.UtcNow));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void An_out_of_range_final_score_is_rejected(decimal finalScore)
    {
        var clock = new FakeClock();
        var c = new ScoreComponents(0.80m, 0.50m, 0.40m, 1.00m);

        Should.Throw<ArgumentOutOfRangeException>(() => new Score(
            JobId, RunId, finalScore, c, RankingWeights.Default, null, false, null, clock.UtcNow));
    }

    [Fact]
    public void A_pre_match_excluded_job_may_be_scored_zero_and_suppressed()
    {
        // A scores row may exist with no matches row: a pre-match exclusion is scored, suppressed, reasoned.
        var components = new ScoreComponents(0m, 0m, 0m, 1.00m);

        var score = NewScore(
            components: components,
            finalScore: 0m,
            suppressed: true,
            suppressionReason: "Excluded before matching: employment type not accepted.");

        score.FinalScore.ShouldBe(0m);
        score.Suppressed.ShouldBeTrue();
    }
}
