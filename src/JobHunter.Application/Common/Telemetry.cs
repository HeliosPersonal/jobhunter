using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace JobHunter.Application.Common;

/// <summary>
/// The single <see cref="ActivitySource"/> and <see cref="Meter"/> for the whole pipeline, plus the
/// nine domain instruments declared once here (observability §2). No other file creates a meter or
/// an activity source. Label discipline is enforced by <see cref="TelemetryLabels"/> and asserted in T11.
/// </summary>
public static class Telemetry
{
    public const string ActivitySourceName = "JobHunter.Pipeline";
    public const string MeterName = "JobHunter";

    public static readonly ActivitySource Source = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    // 1
    public static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>("jobhunter.run.duration", "s", "End-to-end Run wall clock");

    // 2
    public static readonly Histogram<double> RunCost =
        Meter.CreateHistogram<double>("jobhunter.run.cost_usd", "USD", "Total LLM spend per Run");

    // 3
    public static readonly Counter<long> JobsDiscovered =
        Meter.CreateCounter<long>("jobhunter.jobs.discovered", "jobs", "Canonical Jobs after dedup");

    // 4
    public static readonly Counter<long> JobsDeduplicated =
        Meter.CreateCounter<long>("jobhunter.jobs.deduplicated", "jobs", "Postings merged into an existing Job");

    // 5
    public static readonly Histogram<double> BatchLatency =
        Meter.CreateHistogram<double>("jobhunter.batch.latency", "s", "Submit -> results retrieved");

    // 6
    public static readonly Counter<long> DigestCards =
        Meter.CreateCounter<long>("jobhunter.digest.cards", "cards", "Cards delivered");

    // 7
    public static readonly Counter<long> SourceFailures =
        Meter.CreateCounter<long>("jobhunter.source.failures", "failures", "Fetch failures by ats_kind and reason");

    // 8
    public static readonly Counter<long> ParseFailures =
        Meter.CreateCounter<long>("jobhunter.llm.parse_failures", "items", "LLM items that failed schema validation");

    // 9 — the share of a board's postings whose content was unchanged since the last fetch (AC-02).
    // Expected ≈ 0.90: most postings are re-seen verbatim every six hours and only bump last_seen_at.
    public static readonly Histogram<double> RawPostingsUnchangedRatio =
        Meter.CreateHistogram<double>(
            "jobhunter.raw_postings.unchanged_ratio", "ratio", "Fraction of a board's postings unchanged since last fetch");
}
