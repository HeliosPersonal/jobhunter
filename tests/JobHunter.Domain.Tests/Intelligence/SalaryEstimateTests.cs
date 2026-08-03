using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Intelligence;

public sealed class SalaryEstimateTests
{
    [Fact]
    public void Valid_estimate_upper_cases_currency_and_keeps_confidence()
    {
        var result = SalaryEstimate.TryCreate(80000m, 120000m, "usd", SalaryPeriod.Year, 0.7m);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Min.ShouldBe(80000m);
        result.Value.Max.ShouldBe(120000m);
        result.Value.Currency.ShouldBe("USD");
        result.Value.Period.ShouldBe(SalaryPeriod.Year);
        result.Value.Confidence.ShouldBe(0.7m);
    }

    [Fact]
    public void Inverted_bounds_are_swapped()
    {
        var result = SalaryEstimate.TryCreate(120000m, 80000m, "USD", SalaryPeriod.Year, 0.5m);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Min.ShouldBe(80000m);
        result.Value.Max.ShouldBe(120000m);
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(1.5, 1)]
    public void Confidence_is_clamped_to_the_unit_interval(double confidence, double expected)
    {
        var result = SalaryEstimate.TryCreate(100m, 200m, "USD", SalaryPeriod.Year, (decimal)confidence);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Confidence.ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("usdd")]
    [InlineData("12A")]
    [InlineData(" ")]
    public void A_malformed_currency_is_a_failure(string currency)
    {
        var result = SalaryEstimate.TryCreate(100m, 200m, currency, SalaryPeriod.Year, 0.5m);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SalaryEstimate.BadCurrency.Code);
    }

    [Fact]
    public void A_negative_amount_is_a_failure()
    {
        var result = SalaryEstimate.TryCreate(-1m, 200m, "USD", SalaryPeriod.Year, 0.5m);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SalaryEstimate.NegativeAmount.Code);
    }

    [Fact]
    public void Comparing_across_currencies_throws()
    {
        var usd = SalaryEstimate.TryCreate(100m, 200m, "USD", SalaryPeriod.Year, 0.5m).Value;
        var eur = SalaryEstimate.TryCreate(100m, 200m, "EUR", SalaryPeriod.Year, 0.5m).Value;

        Should.Throw<InvalidOperationException>(() => usd.CompareTo(eur));
    }

    [Fact]
    public void Comparing_across_periods_throws()
    {
        var year = SalaryEstimate.TryCreate(100m, 200m, "USD", SalaryPeriod.Year, 0.5m).Value;
        var month = SalaryEstimate.TryCreate(100m, 200m, "USD", SalaryPeriod.Month, 0.5m).Value;

        Should.Throw<InvalidOperationException>(() => year.CompareTo(month));
    }

    [Fact]
    public void Comparable_estimates_order_by_midpoint()
    {
        var low = SalaryEstimate.TryCreate(100m, 200m, "USD", SalaryPeriod.Year, 0.5m).Value;
        var high = SalaryEstimate.TryCreate(300m, 400m, "USD", SalaryPeriod.Year, 0.5m).Value;

        low.CompareTo(high).ShouldBeLessThan(0);
        high.CompareTo(low).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Value_equality_holds_by_components()
    {
        var a = SalaryEstimate.TryCreate(100m, 200m, "USD", SalaryPeriod.Year, 0.5m).Value;
        var b = SalaryEstimate.TryCreate(100m, 200m, "USD", SalaryPeriod.Year, 0.5m).Value;

        a.ShouldBe(b);
    }
}
