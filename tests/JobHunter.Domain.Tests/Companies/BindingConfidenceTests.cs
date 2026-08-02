using JobHunter.Domain.Companies;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Companies;

public sealed class BindingConfidenceTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.80)]
    [InlineData(1.0)]
    public void Accepts_values_in_the_unit_interval(double value)
    {
        BindingConfidence.TryCreate((decimal)value).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(2.0)]
    public void Rejects_values_outside_zero_to_one(double value)
    {
        var result = BindingConfidence.TryCreate((decimal)value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(BindingConfidence.OutOfRange);
    }

    [Fact]
    public void Rounds_to_two_decimal_places()
    {
        BindingConfidence.TryCreate(0.834m).Value.Value.ShouldBe(0.83m);
        BindingConfidence.TryCreate(0.835m).Value.Value.ShouldBe(0.84m);
    }

    [Theory]
    [InlineData(0.79, false)]
    [InlineData(0.80, true)]
    [InlineData(0.95, true)]
    public void IsConfident_reflects_the_discovery_threshold(double value, bool expected)
    {
        BindingConfidence.TryCreate((decimal)value).Value.IsConfident.ShouldBe(expected);
    }

    [Fact]
    public void Discovery_threshold_is_zero_point_eight()
    {
        BindingConfidence.DiscoveryThreshold.ShouldBe(0.80m);
    }

    [Fact]
    public void Equal_confidences_are_equal_and_format_to_two_places()
    {
        var a = BindingConfidence.TryCreate(0.9m).Value;
        var b = BindingConfidence.TryCreate(0.90m).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ToString().ShouldBe("0.90");
    }
}
