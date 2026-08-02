using JobHunter.Application.Common;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests;

public sealed class TelemetryLabelsTests
{
    [Theory]
    [InlineData("stage")]
    [InlineData("ats_kind")]
    [InlineData("tier")]
    [InlineData("environment")]
    [InlineData("outcome")]
    public void Allowed_labels_are_recognised(string label)
    {
        TelemetryLabels.IsAllowed(label).ShouldBeTrue();
    }

    [Theory]
    [InlineData("job_id")]
    [InlineData("company_id")]
    [InlineData("run_id")]
    [InlineData("Stage")]
    [InlineData("")]
    public void Id_shaped_and_unknown_labels_are_rejected(string label)
    {
        // Ids would be unbounded-cardinality labels; they belong on spans, not metrics.
        TelemetryLabels.IsAllowed(label).ShouldBeFalse();
    }

    private static readonly string[] ExpectedKeys = ["stage", "ats_kind", "tier", "environment", "outcome"];

    [Fact]
    public void The_allowlist_is_exactly_the_five_documented_keys()
    {
        TelemetryLabels.Allowed.Count.ShouldBe(5);
        TelemetryLabels.Allowed.ShouldBe(ExpectedKeys, ignoreOrder: true);
    }
}
