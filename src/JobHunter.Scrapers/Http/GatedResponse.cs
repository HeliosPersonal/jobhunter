namespace JobHunter.Scrapers.Http;

/// <summary>Why a gated fetch ended the way it did — a value, so no expected outcome is an exception.</summary>
public enum GatedOutcome
{
    /// <summary>A 2xx response whose body is available in <see cref="GatedResponse.Body"/>.</summary>
    Ok,

    /// <summary>The rate budget was spent; retry after <see cref="GatedResponse.RetryAfter"/>.</summary>
    Deferred,

    /// <summary>We declined to fetch (robots, SSRF, insecure scheme); never a provider fault.</summary>
    Refused,

    /// <summary>The provider answered with a non-success status code.</summary>
    HttpError,
}

/// <summary>
/// The classified result of one gated fetch. The adapter maps this onto its stream: <see cref="GatedOutcome.Ok"/>
/// yields postings; anything else yields nothing and the caller reads <see cref="Outcome"/> to log and,
/// on <see cref="GatedOutcome.Deferred"/>, requeue with <see cref="RetryAfter"/>.
/// </summary>
public sealed record GatedResponse(
    GatedOutcome Outcome,
    int StatusCode,
    string? Body,
    string? Reason,
    TimeSpan? RetryAfter)
{
    public static GatedResponse Ok(int statusCode, string body) =>
        new(GatedOutcome.Ok, statusCode, body, null, null);

    public static GatedResponse Deferred(TimeSpan? retryAfter) =>
        new(GatedOutcome.Deferred, 429, null, "rate-deferred", retryAfter);

    public static GatedResponse Refused(string reason) =>
        new(GatedOutcome.Refused, 403, null, reason, null);

    public static GatedResponse HttpError(int statusCode) =>
        new(GatedOutcome.HttpError, statusCode, null, null, null);
}
