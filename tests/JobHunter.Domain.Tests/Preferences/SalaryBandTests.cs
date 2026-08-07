using JobHunter.Domain.Jobs;
using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Preferences;

/// <summary>
/// The pure banding of a published salary into the <see cref="Dimension.SalaryBand"/> vocabulary the
/// preference learner keys on (F7 data-model §preference_weights — <c>150-180k</c>). It is the one
/// <see cref="JobFacts"/> dimension with no source column: the snapshot must derive it. The load-bearing
/// rules mirror the digest's salary discipline (F5 SAD §6.1): band only a USD annual figure, because a
/// fabricated FX rate or a mixed period would produce a band that is a lie; use the range midpoint; and
/// quantise to a fixed 30k-wide, thousands-labelled band so two nearby postings share a value the fitter
/// can aggregate. A figure with no band (absent, non-USD, non-annual) is <c>null</c>, not a guess.
/// </summary>
public sealed class SalaryBandTests
{
    private static SalaryRange Usd(decimal? min, decimal? max, SalaryPeriod period = SalaryPeriod.Year) =>
        SalaryRange.TryCreate(min, max, "USD", period).Value;

    [Fact]
    public void A_null_salary_has_no_band()
    {
        SalaryBand.Of(null).ShouldBeNull();
    }

    [Theory]
    // The midpoint lands the band: 165k -> 150-180k, matching the F7 data-model example verbatim.
    [InlineData(150_000, 180_000, "150-180k")]
    // A midpoint mid-band still floors to its 30k boundary.
    [InlineData(160_000, 170_000, "150-180k")]
    // A point range (one published figure) bands by that figure.
    [InlineData(200_000, 200_000, "180-210k")]
    // A small figure floors to the first band, never negative.
    [InlineData(10_000, 20_000, "0-30k")]
    // A figure exactly on a boundary opens the next band.
    [InlineData(180_000, 180_000, "180-210k")]
    public void A_usd_annual_salary_bands_by_its_midpoint(int min, int max, string expected)
    {
        SalaryBand.Of(Usd(min, max)).ShouldBe(expected);
    }

    [Fact]
    public void A_non_usd_salary_has_no_band_rather_than_a_converted_one()
    {
        // The digest never fabricates an FX rate (F5 SAD §6.1); neither does the band.
        SalaryBand.Of(SalaryRange.TryCreate(150_000m, 180_000m, "EUR", SalaryPeriod.Year).Value).ShouldBeNull();
    }

    [Theory]
    [InlineData(SalaryPeriod.Month)]
    [InlineData(SalaryPeriod.Day)]
    [InlineData(SalaryPeriod.Hour)]
    public void A_non_annual_salary_has_no_band(SalaryPeriod period)
    {
        // A monthly or hourly figure banded on the annual scale would be a lie; leave it unbanded.
        SalaryBand.Of(Usd(150_000, 180_000, period)).ShouldBeNull();
    }

    // ---- F10 T08: the USD bands wholly below an explicit salary floor (the /floor override, AC-05) ----

    [Fact]
    public void The_bands_wholly_below_a_floor_are_every_band_whose_top_does_not_exceed_it()
    {
        // A floor of 150k USD: the 120-150k band's top just reaches the floor (a job there does not clear it),
        // so it is wholly below; the 150-180k band's top exceeds it, so it is not. Mirrors the suppression rule.
        SalaryBand.BandsWhollyBelow(150_000m)
            .ShouldBe(["0-30k", "30-60k", "60-90k", "90-120k", "120-150k"]);
    }

    [Fact]
    public void A_floor_on_a_band_boundary_includes_that_band_and_excludes_the_next()
    {
        // 90k floor: the 60-90k band's top is exactly the floor (wholly below); 90-120k opens above it.
        SalaryBand.BandsWhollyBelow(90_000m).ShouldBe(["0-30k", "30-60k", "60-90k"]);
    }

    [Fact]
    public void A_floor_below_the_first_band_top_leaves_no_band_wholly_below_it()
    {
        // Nothing sits wholly below a 20k floor — even the first band's top (30k) exceeds it.
        SalaryBand.BandsWhollyBelow(20_000m).ShouldBeEmpty();
    }

    [Fact]
    public void A_zero_or_negative_floor_has_no_bands_below_it()
    {
        SalaryBand.BandsWhollyBelow(0m).ShouldBeEmpty();
        SalaryBand.BandsWhollyBelow(-1m).ShouldBeEmpty();
    }
}
