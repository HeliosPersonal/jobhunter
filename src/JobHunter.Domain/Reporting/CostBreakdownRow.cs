namespace JobHunter.Domain.Reporting;

/// <summary>
/// One <c>(stage, tier)</c> line of a month's spend (F10 T09, <c>/cost</c>): the estimated and actual dollars
/// the cascade booked for that pipeline stage at that model tier. Both figures ride together so the command can
/// flag estimate-vs-actual drift — a stale pricing table surfaces as an estimate that no longer tracks the
/// actual (LedgerEntryKind NFR: within 20%). <see cref="Stage"/> and <see cref="Tier"/> are the persisted
/// <c>text</c> enum names (e.g. <c>"Enrichment"</c>, <c>"Deep"</c>), grouped across every Run in the window.
/// </summary>
public sealed record CostBreakdownRow(
    string Stage,
    string Tier,
    decimal EstimatedUsd,
    decimal ActualUsd);
