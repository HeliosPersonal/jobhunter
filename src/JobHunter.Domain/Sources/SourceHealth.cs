namespace JobHunter.Domain.Sources;

/// <summary>
/// A single provider's fetch health over a trailing window (F10 T09, <c>/sources</c>): how many attempts its
/// boards made, how many succeeded, and when it was last tried. Flat by design — the operations reply lists one
/// line per ATS provider so the Owner can see at a glance which integration is failing, without opening Grafana.
/// <see cref="AtsKind"/> is the persisted <c>text</c> value (e.g. <c>"Greenhouse"</c>), grouped across every
/// source that provider backs. Read-only projection over <c>source_fetch_log</c>; never a per-source dump.
/// </summary>
public sealed record SourceHealth(
    string AtsKind,
    int Attempts,
    int Successes,
    DateTimeOffset LastAttemptAt);
