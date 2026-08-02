using System.ComponentModel.DataAnnotations;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// Every outbound-hygiene knob the shared HTTP pipeline enforces (security §4, SAD §8). Bound and
/// validated at startup via <c>.Validate().ValidateOnStart()</c> — a missing user-agent or a
/// non-positive cap fails the pod at boot, never silently at first fetch (coding-standards §3).
/// </summary>
public sealed class PolitenessOptions
{
    public const string SectionName = "Politeness";

    /// <summary>The 10 MB response cap in bytes (security §4). A larger response is refused, not buffered.</summary>
    public const long DefaultMaxResponseBytes = 10L * 1024 * 1024;

    /// <summary>Identify honestly on every request (security §4, invariant 10).</summary>
    [Required(AllowEmptyStrings = false)]
    public string UserAgent { get; init; } =
        "JobHunter/1.0 (+https://github.com/jobhunter/jobhunter; contact@jobhunter.dev)";

    /// <summary>Default per-host budget; a source may declare a lower rate. Must be positive.</summary>
    [Range(1, 1000)]
    public int DefaultRequestsPerSecond { get; init; } = 1;

    /// <summary>Response body ceiling; a larger response is abandoned before full buffering.</summary>
    [Range(1, long.MaxValue)]
    public long MaxResponseBytes { get; init; } = DefaultMaxResponseBytes;

    /// <summary>How long a parsed <c>robots.txt</c> is trusted before it is re-fetched (SAD §8).</summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan RobotsCacheDuration { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Per-request timeout; no unbounded wait (security §4).</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The Redis key prefix for token buckets: <c>{env}:jobhunter:ratelimit:{host}</c> (SAD §8).</summary>
    public string RateLimitKeyPrefix { get; init; } = "jobhunter:ratelimit";
}
