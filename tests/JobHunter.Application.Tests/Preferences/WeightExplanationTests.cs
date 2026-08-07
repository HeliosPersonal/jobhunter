using JobHunter.Application.Preferences;
using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// T08 (AC-03, QG-1): a learned weight renders as one plain sentence quoting the rate and count of the
/// reaction that produced it — the "34 of your last 38 ignores were below 170k EUR" of
/// [[adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]]. The count comes from the stored
/// <see cref="PreferenceWeight.PositiveRate"/> and <see cref="PreferenceWeight.SupportingSignalCount"/>, so
/// the sentence stays stable after the evidence window moves on, and the direction is read from the rate: a
/// rate below half is the Owner reacting <em>against</em> the value, at or above half reacting toward it.
/// The renderer is pure and emits plain text — the Telegram layer escapes it, the API returns it verbatim.
/// </summary>
public sealed class WeightExplanationTests
{
    private static readonly Guid Model = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset When = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<Guid> Signals(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => Guid.NewGuid())];

    private static PreferenceWeight Weight(
        Dimension dimension, string value, decimal weight, decimal positiveRate, int signalCount) =>
        new(Guid.NewGuid(), Model, dimension, value, weight, Signals(signalCount), positiveRate, When);

    [Fact]
    public void A_negative_weight_reads_as_the_owner_passing_on_the_value()
    {
        // 38 signals, 10.5% engaged → 89.5% passed → round(0.895 × 38) = 34 passed, exactly the ADR example.
        var weight = Weight(Dimension.Country, "DE", weight: -0.79m, positiveRate: 0.105m, signalCount: 38);

        WeightExplanation.Describe(weight)
            .ShouldBe("You passed on 34 of the last 38 roles in DE.");
    }

    [Fact]
    public void A_positive_weight_reads_as_the_owner_engaging_with_the_value()
    {
        // 15 signals, 80% engaged → round(0.8 × 15) = 12 engaged.
        var weight = Weight(Dimension.Technology, "Kafka", weight: 0.6m, positiveRate: 0.8m, signalCount: 15);

        WeightExplanation.Describe(weight)
            .ShouldBe("You engaged with 12 of the last 15 roles using Kafka.");
    }

    [Fact]
    public void The_count_quotes_the_dominant_reaction_rounded_to_the_nearest_whole_signal()
    {
        // round(0.667 × 3) = 2 of the last 3 — the evidence floor still renders a whole, honest count.
        var weight = Weight(Dimension.RemotePolicy, "Remote", weight: 0.33m, positiveRate: 0.667m, signalCount: 3);

        WeightExplanation.Describe(weight)
            .ShouldBe("You engaged with 2 of the last 3 remote roles.");
    }

    [Theory]
    [InlineData(Dimension.SalaryBand, "150-180k", "roles in the 150-180k salary band")]
    [InlineData(Dimension.Country, "DE", "roles in DE")]
    [InlineData(Dimension.CompanySize, "SeriesB", "roles at SeriesB companies")]
    [InlineData(Dimension.Technology, "Kafka", "roles using Kafka")]
    [InlineData(Dimension.TimezoneBand, "CET", "roles in the CET timezone")]
    [InlineData(Dimension.RemotePolicy, "Remote", "remote roles")]
    [InlineData(Dimension.EmploymentType, "FullTime", "FullTime roles")]
    [InlineData(Dimension.AiUsage, "High", "roles with High AI usage")]
    [InlineData(Dimension.RoleFamily, "AiPlatform", "AiPlatform roles")]
    public void Each_dimension_names_its_value_in_natural_language(
        Dimension dimension, string value, string expectedPhrase)
    {
        var weight = Weight(dimension, value, weight: -0.6m, positiveRate: 0.2m, signalCount: 10);

        WeightExplanation.Describe(weight)
            .ShouldContain(expectedPhrase);
    }

    [Fact]
    public void A_rate_of_exactly_half_reads_as_engagement_not_avoidance()
    {
        // The boundary belongs to engagement: an evenly-reacted value would not have earned a weight at all,
        // so if one is being explained the direction defaults to the inclusive side rather than inventing avoidance.
        var weight = Weight(Dimension.Country, "DE", weight: 0m, positiveRate: 0.5m, signalCount: 8);

        WeightExplanation.Describe(weight)
            .ShouldStartWith("You engaged with");
    }
}
