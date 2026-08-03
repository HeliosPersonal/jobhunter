namespace JobHunter.Domain.Jobs;

/// <summary>
/// How precise a job's <c>posted_at</c> is (data-model §jobs <c>posted_at_granularity</c>). Persisted
/// as <c>text</c>, never an ordinal (coding-standards §5). Some providers (Workable) publish a date
/// only, so recording the precision keeps "posted today" honest rather than inventing a time.
/// </summary>
public enum PostedAtGranularity
{
    /// <summary>The provider gave an exact instant.</summary>
    Exact,

    /// <summary>The provider gave a date only; the time component is not meaningful.</summary>
    Day,
}
