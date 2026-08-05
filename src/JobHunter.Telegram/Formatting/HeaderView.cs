namespace JobHunter.Telegram.Formatting;

/// <summary>
/// Which of the four header shapes a digest renders (F5 message contract §Header, §Degraded-day variants).
/// Every variant still arrives at 07:00 (ADR-F5-0001) — a degraded day is a different message, never a
/// missing one. Persisted nowhere; it is a render-time classification the delivery layer sets from the
/// digest's own counts and run outcome.
/// </summary>
public enum DigestMode
{
    /// <summary>The normal morning: counts, the single best opportunity, the hidden line.</summary>
    Full,

    /// <summary>Nothing matched (AC-05): the "this is normal, everything is working" reassurance.</summary>
    NothingNew,

    /// <summary>Analysis did not finish (AC-06): a "(partial)" header and a "still being analysed" line.</summary>
    Partial,

    /// <summary>The daily budget was reached mid-run (AC-06 cost abort): a "(reduced)" header.</summary>
    BudgetReached,
}

/// <summary>
/// The display projection of a <see cref="Domain.Reporting.Digest"/>'s header — the three-second message
/// (F5 message contract §Header). Six lines maximum in every variant (AC-01), which the
/// <see cref="DigestHeaderFormatter"/> guarantees structurally. It carries only counts, one salary
/// statistic and — for a full day — the single best opportunity, all of which the Owner already sees at a
/// glance; nothing about the Owner (the CV crosses exactly one boundary, and it is not this one).
/// </summary>
/// <param name="Mode">Which header shape to render.</param>
/// <param name="TotalNewJobs">New roles discovered in the run window.</param>
/// <param name="StrongMatches">Scores at or above the card threshold.</param>
/// <param name="AvgSalaryUsdThousands">The average advertised USD salary in whole units, or null when too few carry one.</param>
/// <param name="CompaniesChecked">Companies scanned — only shown on a <see cref="DigestMode.NothingNew"/> day.</param>
/// <param name="AnalysedCount">Scores analysed before a budget abort — only shown on a <see cref="DigestMode.BudgetReached"/> day.</param>
/// <param name="CardCount">Cards below the header, for the hidden line.</param>
/// <param name="HiddenCount">Scores suppressed, for the hidden line.</param>
/// <param name="HiddenReasons">The top suppression reasons, summarised in the hidden line (e.g. "salary floor, timezone").</param>
/// <param name="StillAnalysing">Items carried over to tomorrow — the "still being analysed" count on a partial day.</param>
/// <param name="TopOpportunity">The single best opportunity shown in a full header, or null when there is none.</param>
public sealed record HeaderView(
    DigestMode Mode,
    int TotalNewJobs,
    int StrongMatches,
    int? AvgSalaryUsdThousands,
    int CompaniesChecked,
    int AnalysedCount,
    int CardCount,
    int HiddenCount,
    IReadOnlyList<string> HiddenReasons,
    int StillAnalysing,
    HeaderOpportunity? TopOpportunity);

/// <summary>
/// The best opportunity of the day, promoted into the header so that if there is exactly one thing worth
/// seeing it is above the fold (F5 message contract §Header). Two lines: the title with company and score,
/// then a short technology summary.
/// </summary>
/// <param name="Title">The role title, truncated like a card title.</param>
/// <param name="Company">The company name.</param>
/// <param name="Score">The final 0–100 score, shown whole.</param>
/// <param name="Highlights">The technology highlights joined with " · " (e.g. "Kafka · Azure · distributed systems").</param>
public sealed record HeaderOpportunity(
    string Title,
    string Company,
    decimal Score,
    IReadOnlyList<string> Highlights);
