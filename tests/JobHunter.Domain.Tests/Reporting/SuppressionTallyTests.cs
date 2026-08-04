using JobHunter.Domain.Reporting;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Reporting;

public sealed class SuppressionTallyTests
{
    [Fact]
    public void A_valid_tally_exposes_its_reason_and_count()
    {
        var tally = SuppressionTally.TryCreate("Below your salary floor", 34);

        tally.IsSuccess.ShouldBeTrue();
        tally.Value.Reason.ShouldBe("Below your salary floor");
        tally.Value.Count.ShouldBe(34);
    }

    [Fact]
    public void A_zero_count_is_allowed()
    {
        SuppressionTally.TryCreate("Some reason", 0).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_reason_is_trimmed()
    {
        var tally = SuppressionTally.TryCreate("  Below your salary floor  ", 3);

        tally.Value.Reason.ShouldBe("Below your salary floor");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_reason_is_rejected(string? reason)
    {
        var result = SuppressionTally.TryCreate(reason, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SuppressionTally.BlankReason);
    }

    [Fact]
    public void A_negative_count_is_rejected()
    {
        var result = SuppressionTally.TryCreate("Some reason", -1);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SuppressionTally.NegativeCount);
    }

    [Fact]
    public void Equality_is_by_reason_and_count()
    {
        var a = SuppressionTally.TryCreate("Below floor", 5).Value;
        var b = SuppressionTally.TryCreate("Below floor", 5).Value;
        var c = SuppressionTally.TryCreate("Below floor", 6).Value;

        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
