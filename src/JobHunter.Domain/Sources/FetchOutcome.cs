namespace JobHunter.Domain.Sources;

/// <summary>
/// The result of a single fetch attempt (data-model §source_fetch_log <c>outcome</c>). Persisted as
/// <c>text</c>. Every attempt, successful or not, is recorded (AC-11), which is what makes source
/// health answerable from stored data rather than from log retention.
/// </summary>
public enum FetchOutcome
{
    /// <summary>The board answered and its payload parsed.</summary>
    Success,

    /// <summary>The host asked us to slow down (HTTP 429 or a rate budget refusal).</summary>
    RateLimited,

    /// <summary>robots.txt disallowed the path.</summary>
    RobotsDenied,

    /// <summary>A non-success HTTP status other than 429.</summary>
    HttpError,

    /// <summary>The request never completed (DNS, TLS, timeout, connection reset).</summary>
    TransportError,

    /// <summary>The response arrived but could not be parsed.</summary>
    ParseError,

    /// <summary>The source was skipped because it is quarantined.</summary>
    Quarantined,
}
