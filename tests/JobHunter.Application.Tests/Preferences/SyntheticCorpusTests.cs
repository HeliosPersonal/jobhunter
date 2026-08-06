using System.Diagnostics;
using JobHunter.Application.Preferences;
using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// F7 T04 C3: the synthetic-behaviour corpus — the regression suite the feature's credibility rests on
/// (CLAUDE.md testing conventions, test-plan §The synthetic-behaviour corpus). A fictional Owner with
/// <em>planted</em> preferences is simulated, signals consistent with them generated, and the pure
/// <see cref="WeightFitter"/> asserted to recover them — including the indifferent Owner, from whom it must
/// recover nothing. Any change to the fitting method must keep all nine profiles green.
/// </summary>
public sealed class SyntheticCorpusTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly FittingOptions Options = new(Now);

    private const int Seed = 20260806;

    private static FittedModel Fit(SyntheticProfile profile, int? count = null) =>
        WeightFitter.Fit(SyntheticOwnerGenerator.Generate(profile, Seed, Now, count), Options);

    private static decimal? WeightOf(FittedModel model, Dimension dimension, string value) =>
        model.Weights.FirstOrDefault(w => w.Dimension == dimension && w.Value == value)?.Weight;

    [Fact]
    public void ClearLowNoise_recovers_the_planted_preferences_pointing_the_right_way()
    {
        var model = Fit(SyntheticProfile.ClearLowNoise);

        // Liked values are positive, disliked negative — direction is what matters, not magnitude.
        WeightOf(model, Dimension.Country, "DE").ShouldNotBeNull().ShouldBeGreaterThan(0m);
        WeightOf(model, Dimension.Country, "IN").ShouldNotBeNull().ShouldBeLessThan(0m);
        WeightOf(model, Dimension.Technology, "Kafka").ShouldNotBeNull().ShouldBeGreaterThan(0m);
        WeightOf(model, Dimension.Technology, "PHP").ShouldNotBeNull().ShouldBeLessThan(0m);
        WeightOf(model, Dimension.RemotePolicy, "Remote").ShouldNotBeNull().ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void ClearHighNoise_still_recovers_direction_though_with_less_confidence()
    {
        var low = Fit(SyntheticProfile.ClearLowNoise);
        var high = Fit(SyntheticProfile.ClearHighNoise);

        // Direction survives 30% noise...
        WeightOf(high, Dimension.Country, "DE").ShouldNotBeNull().ShouldBeGreaterThan(0m);
        WeightOf(high, Dimension.Technology, "Kafka").ShouldNotBeNull().ShouldBeGreaterThan(0m);

        // ...but the recovered positive rate is closer to 0.5 than the low-noise Owner's — noise erodes
        // confidence, not direction (test-plan).
        var lowRate = low.Weights.Single(w => w.Dimension == Dimension.Country && w.Value == "DE").PositiveRate;
        var highRate = high.Weights.Single(w => w.Dimension == Dimension.Country && w.Value == "DE").PositiveRate;
        highRate.ShouldBeLessThan(lowRate);
    }

    [Fact]
    public void Indifferent_produces_no_weights_at_all()
    {
        // The most important negative case: a learner that finds a pattern in noise is worse than none.
        var model = Fit(SyntheticProfile.Indifferent);

        model.Weights.ShouldBeEmpty();
        model.SignalCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Correlated_weights_both_dimensions_but_keeps_the_combined_effect_bounded()
    {
        var model = Fit(SyntheticProfile.Correlated);

        // Both correlated dimensions earn a preference...
        WeightOf(model, Dimension.SalaryBand, "200k+").ShouldNotBeNull().ShouldBeGreaterThan(0m);
        WeightOf(model, Dimension.CompanySize, "Public").ShouldNotBeNull().ShouldBeGreaterThan(0m);

        // ...yet neither dimension's mass exceeds its share, so the correlation is not applied twice (D2).
        foreach (var dimension in model.Weights.Select(w => w.Dimension).Distinct())
        {
            model.Weights.Where(w => w.Dimension == dimension).Sum(w => Math.Abs(w.Weight))
                .ShouldBeLessThanOrEqualTo(Options.MaxDimensionShare + 0.0001m);
        }
    }

    [Fact]
    public void ChangedMind_lets_recency_make_the_recent_preference_win()
    {
        // Old history rejected Remote, recent history prefers it; the 60-day half-life must flip the sign.
        var model = Fit(SyntheticProfile.ChangedMind);

        WeightOf(model, Dimension.RemotePolicy, "Remote").ShouldNotBeNull().ShouldBeGreaterThan(0m);
        WeightOf(model, Dimension.RemotePolicy, "Onsite").ShouldNotBeNull().ShouldBeLessThan(0m);
    }

    [Fact]
    public void SingleDimensionOverwhelming_bounds_the_dominant_dimension_at_its_share()
    {
        var model = Fit(SyntheticProfile.SingleDimensionOverwhelming);

        // The country the Owner overwhelmingly ignores earns a strong negative weight...
        WeightOf(model, Dimension.Country, "IN").ShouldNotBeNull().ShouldBeLessThan(0m);

        // ...but the Country dimension's total mass is capped, so it cannot become the only thing (AC-09).
        model.Weights.Where(w => w.Dimension == Dimension.Country).Sum(w => Math.Abs(w.Weight))
            .ShouldBeLessThanOrEqualTo(Options.MaxDimensionShare + 0.0001m);
    }

    [Fact]
    public void AlmostEverythingIgnored_produces_negative_weights_without_falling_over()
    {
        var model = Fit(SyntheticProfile.AlmostEverythingIgnored);

        model.Weights.ShouldNotBeEmpty();
        model.Weights.ShouldAllBe(w => w.Weight < 0m);
    }

    [Fact]
    public void Sparse_still_fits_but_leaves_the_activation_decision_to_the_learner()
    {
        // The fitter has no activation floor — that is the learner's job (T05). With 50 signals it simply
        // fits what little evidence there is; the count is what the learner will later judge insufficient.
        var model = Fit(SyntheticProfile.Sparse);

        model.SignalCount.ShouldBeLessThan(PreferenceModel.ActivationThreshold);
    }

    [Fact]
    public void OutcomeHeavy_lets_the_consequential_outcomes_dominate_the_card_taps()
    {
        // Many ignores for NL, but a few interviews and offers (weight 4-6 each) outweigh them: the higher
        // evidence weight of a lived outcome wins over a glance at a card (SAD §8).
        var model = Fit(SyntheticProfile.OutcomeHeavy);

        WeightOf(model, Dimension.Country, "NL").ShouldNotBeNull().ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void Every_recovered_weight_cites_at_least_three_signals()
    {
        // 100% of weights cite >= 3 signals, asserted on every row across every substantive profile, never
        // sampled (test-plan NFR validation).
        foreach (var profile in new[]
        {
            SyntheticProfile.ClearLowNoise, SyntheticProfile.ClearHighNoise, SyntheticProfile.Correlated,
            SyntheticProfile.ChangedMind, SyntheticProfile.SingleDimensionOverwhelming,
            SyntheticProfile.AlmostEverythingIgnored, SyntheticProfile.OutcomeHeavy,
        })
        {
            var model = Fit(profile);
            model.Weights.ShouldAllBe(w =>
                w.SupportingSignalIds.Count >= PreferenceWeight.MinSupportingSignals);
        }
    }

    [Fact]
    public void Fitting_five_thousand_signals_completes_under_thirty_seconds()
    {
        // NFR: refit < 30 s for 5 000 signals (test-plan §NFR validation, done-when 7). Generation is
        // excluded from the timed region — only the fit is measured.
        var signals = SyntheticOwnerGenerator.Generate(SyntheticProfile.ClearLowNoise, Seed, Now, count: 5000);

        var stopwatch = Stopwatch.StartNew();
        var model = WeightFitter.Fit(signals, Options);
        stopwatch.Stop();

        output.WriteLine($"Fitted {model.SignalCount} signals in {stopwatch.ElapsedMilliseconds} ms.");
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }
}
