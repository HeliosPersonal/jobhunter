using System.ComponentModel.DataAnnotations;

namespace JobHunter.Infrastructure.Configuration;

/// <summary>
/// The connection strings the platform needs to reach its hard dependencies. Bound and validated at
/// startup via <c>.Validate().ValidateOnStart()</c>; a missing value fails the pod's readiness probe
/// at boot, never silently at first use (coding-standards §3, AC-09).
/// </summary>
public sealed class ConnectionStringOptions
{
    public const string SectionName = "ConnectionStrings";

    /// <summary>PostgreSQL — the single store of record.</summary>
    [Required(AllowEmptyStrings = false)]
    public string JobHunter { get; init; } = string.Empty;

    /// <summary>RabbitMQ AMQP URI — the inter-stage transport.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Messaging { get; init; } = string.Empty;

    /// <summary>Redis — rate buckets, dedup filter, cache. Optional: the system degrades to DB-backed paths.</summary>
    public string? Cache { get; init; }

    /// <summary>True when the required connection strings are present. Reports the offending key.</summary>
    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(JobHunter))
        {
            error = $"{SectionName}:{nameof(JobHunter)} is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Messaging))
        {
            error = $"{SectionName}:{nameof(Messaging)} is required.";
            return false;
        }

        error = null;
        return true;
    }
}
