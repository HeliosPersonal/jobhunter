using System.Globalization;
using JobHunter.Application.Ranking;
using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ranking;

/// <summary>
/// The pure ranking function (T07, ADR-F4-0001, T14). Every property here is a property of arithmetic, not
/// of a run: determinism (QG-3), component reconciliation (QG-1), the specified freshness decay, weight
/// renormalisation when no preference model is present, and a deterministic tie-break by job id.
/// </summary>
public sealed class ScoreCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_components_always_reconcile_to_the_final_score()
    {
        var result = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), MatchScore: 80),
            alignment: 0.70m,
            preference: 0.50m,
            hasEnrichment: true,
            firstSeenAt: Now,
            now: Now,
            weights: RankingWeights.Default);

        // QG-1: the stored components and the effective weights rebuild the total exactly.
        result.FinalScore.ShouldBe(result.Components.Reconcile(result.EffectiveWeights));

        // And a Score aggregate — whose constructor re-checks reconciliation — accepts it.
        Should.NotThrow(() => new Score(
            result.JobId, Guid.NewGuid(), result.FinalScore, result.Components, result.EffectiveWeights,
            preferenceModelId: null, suppressed: false, suppressionReason: null, computedAt: Now));
    }

    [Fact]
    public void The_alignment_component_is_stored_as_passed()
    {
        var result = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), alignment: 0.85m, preference: 0.5m,
            hasEnrichment: true, Now, Now, RankingWeights.Default);

        result.Components.Alignment.ShouldBe(0.85m);
    }

    [Fact]
    public void A_full_fit_full_alignment_full_preference_and_freshness_with_an_enrichment_scores_one_hundred()
    {
        var result = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), MatchScore: 100),
            alignment: 1.00m,
            preference: 1.00m,
            hasEnrichment: true,
            firstSeenAt: Now,
            now: Now,
            weights: RankingWeights.Default);

        result.FinalScore.ShouldBe(100m);
        result.Components.ConfidenceMultiplier.ShouldBe(1.00m);
    }

    [Fact]
    public void The_anti_goal_multiplier_defaults_to_one_and_is_stored_when_supplied()
    {
        var ordinary = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), 0.7m, 0.5m, hasEnrichment: true, Now, Now, RankingWeights.Default);
        var penalised = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), 0.7m, 0.5m, hasEnrichment: true, Now, Now, RankingWeights.Default,
            antiGoalMultiplier: 0.50m);

        ordinary.Components.AntiGoalMultiplier.ShouldBe(1.00m);
        penalised.Components.AntiGoalMultiplier.ShouldBe(0.50m);
    }

    [Fact]
    public void The_anti_goal_multiplier_scales_the_final_score_and_still_reconciles()
    {
        var ordinary = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), 0.7m, 0.5m, hasEnrichment: true, Now, Now, RankingWeights.Default);
        var penalised = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), 0.7m, 0.5m, hasEnrichment: true, Now, Now, RankingWeights.Default,
            antiGoalMultiplier: 0.50m);

        // A multiplier like confidence: the whole total halves, and the stored components still rebuild it (QG-1).
        penalised.FinalScore.ShouldBe(ordinary.FinalScore * 0.50m);
        penalised.FinalScore.ShouldBe(penalised.Components.Reconcile(penalised.EffectiveWeights));
    }

    [Fact]
    public void A_missing_enrichment_lowers_the_confidence_multiplier_to_the_documented_value()
    {
        var withEnrichment = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), 0.7m, 0.5m, hasEnrichment: true, Now, Now, RankingWeights.Default);
        var withoutEnrichment = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), 0.7m, 0.5m, hasEnrichment: false, Now, Now, RankingWeights.Default);

        withEnrichment.Components.ConfidenceMultiplier.ShouldBe(1.00m);
        withoutEnrichment.Components.ConfidenceMultiplier.ShouldBe(0.85m);
        // A multiplier, so uncertainty lowers rather than excludes (AC-09).
        withoutEnrichment.FinalScore.ShouldBe(withEnrichment.FinalScore * 0.85m);
    }

    [Theory]
    [InlineData(0, 1.00)]
    [InlineData(3, 0.65)]
    [InlineData(7, 0.37)]
    [InlineData(14, 0.14)]
    public void Freshness_decays_as_the_specification_says(int ageDays, double expected)
    {
        var result = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), 0.7m, 0.5m, hasEnrichment: true,
            firstSeenAt: Now.AddDays(-ageDays), now: Now, weights: RankingWeights.Default);

        ((double)result.Components.Freshness).ShouldBe(expected, tolerance: 0.01);
    }

    [Fact]
    public void A_job_first_seen_in_the_future_is_capped_at_full_freshness()
    {
        var result = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), 0.7m, 0.5m, hasEnrichment: true,
            firstSeenAt: Now.AddDays(2), now: Now, weights: RankingWeights.Default);

        result.Components.Freshness.ShouldBe(1.00m);
    }

    [Fact]
    public void A_very_old_job_decays_toward_zero_freshness_without_going_negative()
    {
        var result = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), 0.7m, 0.5m, hasEnrichment: true,
            firstSeenAt: Now.AddDays(-3650), now: Now, weights: RankingWeights.Default);

        result.Components.Freshness.ShouldBeInRange(0m, 0.0001m);
    }

    [Fact]
    public void With_no_preference_model_the_remaining_weights_renormalise_to_sum_to_one()
    {
        var result = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), alignment: 0.7m, preference: null,
            hasEnrichment: true, Now, Now, RankingWeights.Default);

        // The 0.20 preference weight is redistributed across match, alignment and freshness in proportion
        // (remaining = 0.45 + 0.20 + 0.15 = 0.80), so the effective weights still sum to 1 and the
        // preference component contributes nothing.
        result.EffectiveWeights.Preference.ShouldBe(0m);
        result.Components.Preference.ShouldBe(0m);
        result.EffectiveWeights.Match.ShouldBe(0.45m / 0.80m, tolerance: 0.0001m);
        result.EffectiveWeights.Alignment.ShouldBe(0.20m / 0.80m, tolerance: 0.0001m);
        result.EffectiveWeights.Freshness.ShouldBe(0.15m / 0.80m, tolerance: 0.0001m);
        (result.EffectiveWeights.Match + result.EffectiveWeights.Alignment
            + result.EffectiveWeights.Preference + result.EffectiveWeights.Freshness)
            .ShouldBe(1m, tolerance: 0.0001m);
        result.PreferencePresent.ShouldBeFalse();
    }

    [Fact]
    public void With_a_preference_model_the_passed_weights_are_used_unchanged()
    {
        var result = ScoreCalculator.Calculate(
            new MatchFacts(Guid.NewGuid(), 80), alignment: 0.7m, preference: 0.4m,
            hasEnrichment: true, Now, Now, RankingWeights.Default);

        result.EffectiveWeights.ShouldBe(RankingWeights.Default);
        result.Components.Preference.ShouldBe(0.4m);
        result.PreferencePresent.ShouldBeTrue();
    }

    [Fact]
    public void A_stronger_alignment_raises_the_final_score_all_else_equal()
    {
        var jobId = Guid.NewGuid();
        var low = ScoreCalculator.Calculate(
            new MatchFacts(jobId, 80), alignment: 0.20m, preference: 0.5m, true, Now, Now, RankingWeights.Default);
        var high = ScoreCalculator.Calculate(
            new MatchFacts(jobId, 80), alignment: 0.90m, preference: 0.5m, true, Now, Now, RankingWeights.Default);

        high.FinalScore.ShouldBeGreaterThan(low.FinalScore);
    }

    [Fact]
    public void The_function_is_deterministic_over_ten_thousand_inputs_across_cultures_and_orderings()
    {
        var inputs = GenerateInputs(10_000).ToList();

        var reference = inputs.Select(Score).ToList();

        // Same inputs, a different culture, and a shuffled evaluation order must reproduce the totals bit
        // for bit — the function has no clock, no culture and no ordering dependency (QG-3).
        foreach (var culture in new[] { "en-US", "de-DE", "tr-TR" })
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            try
            {
                var shuffled = inputs
                    .Select((input, index) => (input, index))
                    .OrderBy(t => (t.index * 2_654_435_761L) % inputs.Count)
                    .ToList();

                foreach (var (input, index) in shuffled)
                {
                    Score(input).FinalScore.ShouldBe(reference[index].FinalScore);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }

    [Fact]
    public void Ranking_orders_by_final_score_descending_and_breaks_ties_by_job_id()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");

        // Two jobs with an identical final score: the smaller job id must come first, deterministically.
        var a = ScoreCalculator.Calculate(new MatchFacts(high, 80), 0.7m, 0.5m, true, Now, Now, RankingWeights.Default);
        var b = ScoreCalculator.Calculate(new MatchFacts(low, 80), 0.7m, 0.5m, true, Now, Now, RankingWeights.Default);
        var c = ScoreCalculator.Calculate(new MatchFacts(Guid.NewGuid(), 95), 0.7m, 0.5m, true, Now, Now, RankingWeights.Default);

        a.FinalScore.ShouldBe(b.FinalScore);

        var ranked = ScoreCalculator.Rank([a, b, c]).ToList();

        ranked[0].ShouldBe(c);            // highest score first
        ranked[1].JobId.ShouldBe(low);    // tie broken by ascending job id
        ranked[2].JobId.ShouldBe(high);
    }

    private static ScoreResult Score(
        (MatchFacts Match, decimal Alignment, decimal? Preference, bool HasEnrichment, DateTimeOffset FirstSeen) input) =>
        ScoreCalculator.Calculate(
            input.Match, input.Alignment, input.Preference, input.HasEnrichment, input.FirstSeen, Now, RankingWeights.Default);

    private static IEnumerable<(MatchFacts, decimal, decimal?, bool, DateTimeOffset)> GenerateInputs(int count)
    {
        // A fixed seed so the corpus itself is reproducible; the point under test is that scoring it is.
        var random = new Random(20260804);
        for (var i = 0; i < count; i++)
        {
            var match = new MatchFacts(Guid.NewGuid(), random.Next(0, 101));
            var alignment = Math.Round((decimal)random.NextDouble(), 4);
            decimal? preference = random.Next(0, 4) == 0 ? null : Math.Round((decimal)random.NextDouble(), 4);
            var hasEnrichment = random.Next(0, 2) == 0;
            var firstSeen = Now.AddDays(-random.Next(0, 40)).AddHours(-random.Next(0, 24));
            yield return (match, alignment, preference, hasEnrichment, firstSeen);
        }
    }
}
