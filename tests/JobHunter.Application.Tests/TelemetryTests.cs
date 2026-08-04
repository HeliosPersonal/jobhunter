using System.Diagnostics.Metrics;
using JobHunter.Application.Common;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void The_single_activity_source_and_meter_are_named_as_documented()
    {
        Telemetry.Source.Name.ShouldBe(Telemetry.ActivitySourceName);
        Telemetry.ActivitySourceName.ShouldBe("JobHunter.Pipeline");
        Telemetry.MeterName.ShouldBe("JobHunter");
    }

    [Fact]
    public void All_domain_instruments_are_declared_on_the_one_meter()
    {
        var instruments = new Instrument[]
        {
            Telemetry.RunDuration,
            Telemetry.RunCost,
            Telemetry.JobsDiscovered,
            Telemetry.JobsDeduplicated,
            Telemetry.BatchLatency,
            Telemetry.DigestCards,
            Telemetry.SourceFailures,
            Telemetry.ParseFailures,
            Telemetry.RawPostingsUnchangedRatio,
            Telemetry.IndexDrift,
            Telemetry.MatchScoreDistribution,
            Telemetry.RankingSuppressed,
        };

        instruments.Length.ShouldBe(12);
        instruments.ShouldAllBe(i => i.Meter.Name == Telemetry.MeterName);
        instruments.Select(i => i.Name).Distinct().Count().ShouldBe(12);
    }

    [Fact]
    public void Instrument_names_are_dot_namespaced_under_jobhunter()
    {
        var names = new[]
        {
            Telemetry.RunDuration.Name,
            Telemetry.RunCost.Name,
            Telemetry.JobsDiscovered.Name,
            Telemetry.JobsDeduplicated.Name,
            Telemetry.BatchLatency.Name,
            Telemetry.DigestCards.Name,
            Telemetry.SourceFailures.Name,
            Telemetry.ParseFailures.Name,
            Telemetry.RawPostingsUnchangedRatio.Name,
            Telemetry.IndexDrift.Name,
            Telemetry.MatchScoreDistribution.Name,
            Telemetry.RankingSuppressed.Name,
        };

        names.ShouldAllBe(n => n.StartsWith("jobhunter.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_counter_records_without_throwing_when_no_listener_is_attached()
    {
        // Instruments must be safe to record against even with no MeterListener (AC-06 spirit).
        Should.NotThrow(() => Telemetry.JobsDiscovered.Add(1));
        Should.NotThrow(() => Telemetry.RunCost.Record(0.12));
    }
}
