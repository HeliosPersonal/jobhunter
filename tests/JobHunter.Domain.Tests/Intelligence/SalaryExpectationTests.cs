using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Intelligence;

public sealed class SalaryExpectationTests
{
    [Fact]
    public void A_valid_expectation_exposes_its_fields()
    {
        var result = SalaryExpectation.TryCreate(100000m, 140000m, "eur");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Min.ShouldBe(100000m);
        result.Value.Max.ShouldBe(140000m);
        result.Value.Currency.ShouldBe("EUR");
    }

    [Fact]
    public void Inverted_bounds_are_swapped()
    {
        var result = SalaryExpectation.TryCreate(140000m, 100000m, "USD");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Min.ShouldBe(100000m);
        result.Value.Max.ShouldBe(140000m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void A_malformed_currency_is_a_failure_not_an_exception(string? currency)
    {
        var result = SalaryExpectation.TryCreate(100000m, 140000m, currency);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SalaryExpectation.BadCurrency);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    public void A_negative_amount_is_a_failure(decimal min, decimal max)
    {
        var result = SalaryExpectation.TryCreate(min, max, "EUR");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SalaryExpectation.NegativeAmount);
    }

    [Fact]
    public void Equal_expectations_are_equal_by_value()
    {
        var a = SalaryExpectation.TryCreate(100000m, 140000m, "EUR").Value;
        var b = SalaryExpectation.TryCreate(100000m, 140000m, "EUR").Value;

        a.ShouldBe(b);
    }

    [Fact]
    public void A_single_point_expectation_renders_without_a_range()
    {
        var one = SalaryExpectation.TryCreate(120000m, 120000m, "EUR").Value;
        var range = SalaryExpectation.TryCreate(100000m, 140000m, "EUR").Value;

        one.ToString().ShouldNotContain("-");
        range.ToString().ShouldContain("-");
    }
}
