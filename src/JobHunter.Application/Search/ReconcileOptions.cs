namespace JobHunter.Application.Search;

/// <summary>
/// The knobs for the nightly reconcile and the full rebuild (F9-T08, SAD §6.3). Bound and validated at
/// startup (coding-standards §2). The defaults encode the design: divergence above one percent is
/// treated as drift worth re-indexing (data-model §Reconciliation), documents are written in batches of
/// two hundred to bound the round trips, and a full rebuild of ten thousand jobs is expected to finish
/// inside ten minutes (AC-10) — a rebuild that overruns that budget is logged as a warning so the NFR is
/// observable rather than silently missed.
/// </summary>
public sealed class ReconcileOptions
{
    public const string SectionName = "Search:Reconcile";

    /// <summary>The fractional divergence above which reconcile re-indexes the live set (default 0.01 = 1%).</summary>
    public double DriftThreshold { get; set; } = 0.01;

    /// <summary>How many documents are upserted per round trip during a reconcile or rebuild.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>The wall-clock budget a full rebuild is expected to finish inside (AC-10).</summary>
    public TimeSpan RebuildBudget { get; set; } = TimeSpan.FromMinutes(10);
}
