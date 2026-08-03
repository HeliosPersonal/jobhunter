using JobHunter.Application.Normalization;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

public sealed class SalaryParserTests
{
    [Fact]
    public void Blank_input_is_none()
    {
        SalaryParser.Parse(null).ShouldBe(ParsedSalary.None);
        SalaryParser.Parse("   ").ShouldBe(ParsedSalary.None);
    }

    [Fact]
    public void Competitive_keeps_raw_only_and_produces_no_range()
    {
        var parsed = SalaryParser.Parse("Competitive");

        parsed.HasStructuredRange.ShouldBeFalse();
        parsed.Range.ShouldBeNull();
        parsed.Raw.ShouldBe("Competitive");
    }

    [Fact]
    public void A_dollar_range_parses_min_max_currency_and_default_period()
    {
        var parsed = SalaryParser.Parse("$120,000 - $160,000");

        parsed.HasStructuredRange.ShouldBeTrue();
        parsed.Range!.Min.ShouldBe(120_000m);
        parsed.Range.Max.ShouldBe(160_000m);
        parsed.Range.Currency.ShouldBe("USD");
        parsed.Range.Period.ShouldBe(SalaryPeriod.Year);
        parsed.Raw.ShouldBe("$120,000 - $160,000");
    }

    [Fact]
    public void K_suffixes_expand_to_thousands()
    {
        var parsed = SalaryParser.Parse("$180K – $220K");

        parsed.Range!.Min.ShouldBe(180_000m);
        parsed.Range.Max.ShouldBe(220_000m);
    }

    [Fact]
    public void An_m_suffix_expands_to_millions()
    {
        SalaryParser.Parse("$1.2M").Range!.Min.ShouldBe(1_200_000m);
    }

    [Fact]
    public void A_single_figure_is_a_point_range()
    {
        var parsed = SalaryParser.Parse("€90,000 per year");

        parsed.Range!.Min.ShouldBe(90_000m);
        parsed.Range.Max.ShouldBe(90_000m);
        parsed.Range.Currency.ShouldBe("EUR");
    }

    [Theory]
    [InlineData("£50 per hour", SalaryPeriod.Hour)]
    [InlineData("£400 / day", SalaryPeriod.Day)]
    [InlineData("£5,000 per month", SalaryPeriod.Month)]
    [InlineData("£80,000 per annum", SalaryPeriod.Year)]
    public void The_period_is_detected(string text, SalaryPeriod period)
    {
        SalaryParser.Parse(text).Range!.Period.ShouldBe(period);
    }

    [Fact]
    public void The_default_period_applies_when_none_is_stated()
    {
        SalaryParser.Parse("$50 range", SalaryPeriod.Hour).Range!.Period.ShouldBe(SalaryPeriod.Hour);
    }

    [Fact]
    public void An_unrecognised_currency_keeps_raw_and_nulls_the_structure()
    {
        var parsed = SalaryParser.Parse("ZAR 500,000");

        parsed.HasStructuredRange.ShouldBeFalse();
        parsed.Raw.ShouldBe("ZAR 500,000");
    }

    [Fact]
    public void A_currency_with_no_number_keeps_raw_only()
    {
        var parsed = SalaryParser.Parse("USD - negotiable");

        parsed.HasStructuredRange.ShouldBeFalse();
        parsed.Raw.ShouldBe("USD - negotiable");
    }

    [Fact]
    public void An_inverted_range_is_swapped_and_the_anomaly_surfaces()
    {
        var parsed = SalaryParser.Parse("$160,000 - $120,000");

        parsed.Range!.Min.ShouldBe(120_000m);
        parsed.Range.Max.ShouldBe(160_000m);
        parsed.Range.MinMaxSwapped.ShouldBeTrue();
    }

    [Fact]
    public void The_currency_code_form_is_recognised()
    {
        SalaryParser.Parse("USD 120000 - 160000").Range!.Currency.ShouldBe("USD");
    }
}
