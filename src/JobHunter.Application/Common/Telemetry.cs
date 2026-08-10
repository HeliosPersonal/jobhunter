using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace JobHunter.Application.Common;

/// <summary>
/// The single <see cref="ActivitySource"/> and <see cref="Meter"/> for the whole pipeline, plus the
/// domain instruments declared once here (observability §2). No other file creates a meter or
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

    // 10 — the fractional divergence between the live-job count in PostgreSQL and the document count in the
    // search index, recorded by the nightly reconcile (F9-T08, SAD §6.3). A gauge, not a counter: the last
    // value is what matters, and drift that does not self-heal after a re-index is what an alert watches for.
    public static readonly Gauge<double> IndexDrift =
        Meter.CreateGauge<double>("jobhunter.index.drift", "ratio", "|live jobs - indexed documents| / live jobs");

    // Ranking (F4 SAD §7): the distribution of final scores a Run produces, and how many jobs it suppressed.
    public static readonly Histogram<double> MatchScoreDistribution =
        Meter.CreateHistogram<double>("jobhunter.match.score_distribution", "score", "Final ranking scores per Run");

    public static readonly Counter<long> RankingSuppressed =
        Meter.CreateCounter<long>("jobhunter.ranking.suppressed", "jobs", "Matched jobs suppressed with a reason");

    // F7 T07 (AC-06): an Owner override contradicted the model's verdict — a never-suppress rule forced a
    // hidden job to appear, or an always-suppress rule hid a shown one. Counted so the tension is visible,
    // never a silent rewrite (invariant 11).
    public static readonly Counter<long> RankingOverrideApplied =
        Meter.CreateCounter<long>("jobhunter.ranking.override_applied", "jobs", "Owner override reversed the model verdict");

    // F7 T09 (done-when 5, risk D3): suppression regret — the latest Run's suppressed jobs the Owner then
    // acted on (retrieved through /hidden and opened, saved or applied to). A gauge, not a counter: the last
    // measured value is what a dashboard watches, and a rising regret is the signal the learned model is
    // over-suppressing (invariant 11), the counterweight to precision@10.
    public static readonly Gauge<long> SuppressionRegret =
        Meter.CreateGauge<long>("jobhunter.preferences.suppression_regret", "jobs", "Latest-Run suppressed jobs the Owner acted on");

    // F4 T20 (done-when 3, D5): weekly ratings-based precision@10 — the share of the previous week's top-ten
    // delivered cards the Owner rated "worth opening". A gauge, not a counter: the last measured week is what a
    // dashboard charts against the ≥0.6 target, and it is the empirical counterpart to the golden ranking set
    // (which proves the ranking stable, not good). Distinct from the F7 engagement-based precision series.
    public static readonly Gauge<double> PrecisionAtTen =
        Meter.CreateGauge<double>("jobhunter.precision_at_10", "ratio", "Latest week's top-ten cards rated worth opening");
}
