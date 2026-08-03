using System.Globalization;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class SalaryRangeTests
{
    [Fact]
    public void A_min_and_max_in_order_are_kept_as_given()
    {
        var range = SalaryRange.TryCreate(80_000m, 120_000m, "USD", SalaryPeriod.Year).Value;

        range.Min.ShouldBe(80_000m);
        range.Max.ShouldBe(120_000m);
        range.Currency.ShouldBe("USD");
        range.Period.ShouldBe(SalaryPeriod.Year);
        range.MinMaxSwapped.ShouldBeFalse();
    }

    [Fact]
    public void A_max_below_min_is_swapped_and_the_anomaly_is_recorded()
    {
        var range = SalaryRange.TryCreate(120_000m, 80_000m, "USD", SalaryPeriod.Year).Value;

        range.Min.ShouldBe(80_000m);
        range.Max.ShouldBe(120_000m);
        range.MinMaxSwapped.ShouldBeTrue();
    }

    [Fact]
    public void A_single_min_becomes_a_point_range()
    {
        var range = SalaryRange.TryCreate(90_000m, null, "EUR", SalaryPeriod.Year).Value;

        range.Min.ShouldBe(90_000m);
        range.Max.ShouldBe(90_000m);
        range.MinMaxSwapped.ShouldBeFalse();
    }

    [Fact]
    public void A_single_max_becomes_a_point_range()
    {
        var range = SalaryRange.TryCreate(null, 90_000m, "EUR", SalaryPeriod.Year).Value;

        range.Min.ShouldBe(90_000m);
        range.Max.ShouldBe(90_000m);
    }

    [Fact]
    public void No_amount_is_a_failure()
    {
        var result = SalaryRange.TryCreate(null, null, "USD", SalaryPeriod.Year);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SalaryRange.NoAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_currency_is_a_failure(string? currency)
    {
        var result = SalaryRange.TryCreate(80_000m, 120_000m, currency, SalaryPeriod.Year);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SalaryRange.BlankCurrency);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("US1")]
    [InlineData("12$")]
    public void A_malformed_currency_is_a_failure(string currency)
    {
        var result = SalaryRange.TryCreate(80_000m, 120_000m, currency, SalaryPeriod.Year);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SalaryRange.BadCurrency);
    }

    [Fact]
    public void A_currency_is_upper_cased_and_trimmed()
    {
        var range = SalaryRange.TryCreate(80_000m, 120_000m, " usd ", SalaryPeriod.Year).Value;

        range.Currency.ShouldBe("USD");
    }

    [Fact]
    public void A_negative_amount_is_a_failure()
    {
        var result = SalaryRange.TryCreate(-1m, 100m, "USD", SalaryPeriod.Year);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SalaryRange.NegativeAmount);
    }

    [Fact]
    public void Comparing_across_currencies_throws()
    {
        var usd = SalaryRange.TryCreate(100_000m, 120_000m, "USD", SalaryPeriod.Year).Value;
        var eur = SalaryRange.TryCreate(100_000m, 120_000m, "EUR", SalaryPeriod.Year).Value;

        Should.Throw<InvalidOperationException>(() => usd.IsHigherThan(eur));
        Should.Throw<InvalidOperationException>(() => usd.CompareTo(eur));
    }

    [Fact]
    public void Comparing_across_periods_throws()
    {
        var yearly = SalaryRange.TryCreate(100_000m, 120_000m, "USD", SalaryPeriod.Year).Value;
        var monthly = SalaryRange.TryCreate(100_000m, 120_000m, "USD", SalaryPeriod.Month).Value;

        Should.Throw<InvalidOperationException>(() => yearly.IsHigherThan(monthly));
        Should.Throw<InvalidOperationException>(() => yearly.CompareTo(monthly));
    }

    [Fact]
    public void A_range_entirely_above_another_is_higher()
    {
        var low = SalaryRange.TryCreate(50_000m, 60_000m, "USD", SalaryPeriod.Year).Value;
        var high = SalaryRange.TryCreate(70_000m, 90_000m, "USD", SalaryPeriod.Year).Value;

        high.IsHigherThan(low).ShouldBeTrue();
        low.IsHigherThan(high).ShouldBeFalse();
    }

    [Fact]
    public void Overlapping_ranges_are_not_higher()
    {
        var a = SalaryRange.TryCreate(50_000m, 80_000m, "USD", SalaryPeriod.Year).Value;
        var b = SalaryRange.TryCreate(70_000m, 90_000m, "USD", SalaryPeriod.Year).Value;

        a.IsHigherThan(b).ShouldBeFalse();
    }

    [Fact]
    public void Compare_orders_by_midpoint()
    {
        var a = SalaryRange.TryCreate(50_000m, 60_000m, "USD", SalaryPeriod.Year).Value;
        var b = SalaryRange.TryCreate(70_000m, 90_000m, "USD", SalaryPeriod.Year).Value;

        a.CompareTo(b).ShouldBeLessThan(0);
        b.CompareTo(a).ShouldBeGreaterThan(0);
        a.CompareTo(a).ShouldBe(0);
    }

    [Fact]
    public void Comparing_against_null_throws()
    {
        var a = SalaryRange.TryCreate(50_000m, 60_000m, "USD", SalaryPeriod.Year).Value;

        Should.Throw<ArgumentNullException>(() => a.IsHigherThan(null!));
    }

    [Fact]
    public void Ranges_with_the_same_components_are_equal()
    {
        var a = SalaryRange.TryCreate(50_000m, 60_000m, "USD", SalaryPeriod.Year).Value;
        var b = SalaryRange.TryCreate(50_000m, 60_000m, "USD", SalaryPeriod.Year).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Ranges_differing_in_currency_are_not_equal()
    {
        var a = SalaryRange.TryCreate(50_000m, 60_000m, "USD", SalaryPeriod.Year).Value;
        var b = SalaryRange.TryCreate(50_000m, 60_000m, "EUR", SalaryPeriod.Year).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void ToString_is_culture_invariant()
    {
        var range = SalaryRange.TryCreate(50_000.5m, 60_000m, "USD", SalaryPeriod.Year).Value;
        var point = SalaryRange.TryCreate(50_000m, null, "USD", SalaryPeriod.Month).Value;

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            range.ToString().ShouldBe("USD 50000.5-60000/Year");
            point.ToString().ShouldBe("USD 50000/Month");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
