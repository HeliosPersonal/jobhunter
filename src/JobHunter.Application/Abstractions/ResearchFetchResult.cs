namespace JobHunter.Application.Abstractions;

/// <summary>
/// Why a guarded research fetch ended the way it did — a value, so no expected outcome is an exception
/// (coding-standards §2). It mirrors F1's <c>GatedOutcome</c> but is research-scoped: the fetch target
/// derives partly from model output, so a refusal (scheme, allowlist, SSRF) is a first-class result the
/// category fetcher records as "category unavailable", never a fault to propagate.
/// </summary>
public enum ResearchFetchOutcome
{
    /// <summary>A 2xx response whose extracted body is in <see cref="ResearchFetchResult.Body"/>.</summary>
    Ok,

    /// <summary>We declined to fetch — non-HTTPS scheme, non-allowlisted host, or a private address.</summary>
    Refused,

    /// <summary>The per-host rate budget was spent; retry after <see cref="ResearchFetchResult.RetryAfter"/>.</summary>
    Deferred,

    /// <summary>The source answered with a non-success status, or too many redirects.</summary>
    HttpError,
}

/// <summary>
/// The classified result of one guarded research fetch (SAD §5, QG-3). The category fetcher maps
/// <see cref="ResearchFetchOutcome.Ok"/> onto a <c>FetchedDocument</c> whose URL is <see cref="FinalUrl"/>
/// — the URL actually retrieved after any redirects, which is what a later claim must cite — and treats
/// every other outcome as no document, reading <see cref="Reason"/> only to log.
/// </summary>
public sealed record ResearchFetchResult(
    ResearchFetchOutcome Outcome,
    string? Body,
    Uri? FinalUrl,
    string? Reason,
    TimeSpan? RetryAfter)
{
    public static ResearchFetchResult Ok(string body, Uri finalUrl) =>
        new(ResearchFetchOutcome.Ok, body, finalUrl, null, null);

    public static ResearchFetchResult Refused(string reason) =>
        new(ResearchFetchOutcome.Refused, null, null, reason, null);

    public static ResearchFetchResult Deferred(TimeSpan? retryAfter) =>
        new(ResearchFetchOutcome.Deferred, null, null, "rate-deferred", retryAfter);

    public static ResearchFetchResult HttpError(string reason) =>
        new(ResearchFetchOutcome.HttpError, null, null, reason, null);
}
