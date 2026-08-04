namespace JobHunter.Application.Profiles;

/// <summary>
/// Tunables for the re-match scheduler (ADR-F4-0002, PRD §6). Bound and validated at startup
/// (coding-standards §options). The window is where a job's realistic chance of still being open meets the
/// cost of re-matching it against the new CV; thirty days is the accepted default (ADR-F4-0002), and it is
/// configuration rather than a literal so the cost/coverage trade-off is tunable without a deploy.
/// </summary>
public sealed class ReMatchOptions
{
    public const string SectionName = "ReMatch";

    /// <summary>
    /// How far back from activation live jobs are re-matched. Default 30 days (ADR-F4-0002). A job first
    /// seen at or after <c>now - Window</c> is queued; one older keeps its stale match until it closes.
    /// </summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromDays(30);
}
