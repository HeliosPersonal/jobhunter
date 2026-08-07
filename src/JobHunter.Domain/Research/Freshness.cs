namespace JobHunter.Domain.Research;

/// <summary>
/// The freshness policy for a dossier (SAD §8, AC-06, risk D5). A dossier ages out after 30 days for most
/// categories; <see cref="ResearchCategory.News"/> and <see cref="ResearchCategory.Layoffs"/> — the two
/// whose value evaporates fastest — refresh at seven. <see cref="IsStale"/> is a pure function of the
/// generation time, the category and the current time: the caller passes the clock reading in (via
/// <c>IClock</c> at the edge), so the policy is deterministic and depends on no ambient time.
/// </summary>
public static class Freshness
{
    private static readonly TimeSpan Default = TimeSpan.FromDays(30);
    private static readonly TimeSpan Volatile = TimeSpan.FromDays(7);

    /// <summary>The staleness threshold for a category: seven days for news and layoffs, thirty otherwise.</summary>
    public static TimeSpan ThresholdFor(ResearchCategory category) =>
        category is ResearchCategory.News or ResearchCategory.Layoffs ? Volatile : Default;

    /// <summary>
    /// Whether a dossier generated at <paramref name="generatedAt"/> is stale for
    /// <paramref name="category"/> as of <paramref name="now"/>. The threshold boundary is inclusive of
    /// fresh — a dossier exactly at its threshold has not yet aged out — and a future generation time (clock
    /// skew) is never stale.
    /// </summary>
    public static bool IsStale(DateTimeOffset generatedAt, ResearchCategory category, DateTimeOffset now) =>
        now - generatedAt > ThresholdFor(category);
}
