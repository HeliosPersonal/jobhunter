using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Preferences;

/// <summary>
/// T01/T02: an Owner override that outranks learning. It is a stated rule, not inferred evidence — so it
/// needs no supporting signals, only a dimension, a value and a direction.
/// </summary>
public sealed class SuppressionOverrideTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_override_records_its_dimension_value_mode_and_time()
    {
        var o = new SuppressionOverride(
            Guid.NewGuid(), Dimension.Country, "DE", SuppressionMode.NeverSuppress, When);

        o.Dimension.ShouldBe(Dimension.Country);
        o.Value.ShouldBe("DE");
        o.Mode.ShouldBe(SuppressionMode.NeverSuppress);
        o.CreatedAt.ShouldBe(When);
    }

    [Fact]
    public void The_value_is_trimmed()
    {
        var o = new SuppressionOverride(
            Guid.NewGuid(), Dimension.Country, "  DE  ", SuppressionMode.AlwaysSuppress, When);

        o.Value.ShouldBe("DE");
    }

    [Fact]
    public void A_blank_value_cannot_be_constructed()
    {
        Should.Throw<ArgumentException>(() => new SuppressionOverride(
            Guid.NewGuid(), Dimension.Country, "   ", SuppressionMode.NeverSuppress, When));
    }
}
