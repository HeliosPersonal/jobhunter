using System.ComponentModel.DataAnnotations;

namespace JobHunter.Search;

/// <summary>
/// The Typesense connection and naming knobs (SAD §2, ADR-0008). Bound and validated at startup via
/// <c>.Validate().ValidateOnStart()</c> — a missing base URL, api key or environment prefix fails the pod
/// at boot, never silently at the first index write (coding-standards §3). The collection name is derived,
/// not configured, so the <c>{env}_jobhunter_</c> helios naming rule cannot be broken by a typo in a
/// setting.
/// </summary>
public sealed class TypesenseOptions
{
    public const string SectionName = "Typesense";

    /// <summary>The Typesense HTTP base URL, e.g. <c>http://typesense.helios:8108</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>The admin api key. A secret — it is never logged (invariant 12).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>The deployment environment prefix, e.g. <c>production</c> or <c>local</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string EnvironmentPrefix { get; init; } = string.Empty;

    /// <summary>Per-request timeout; no unbounded wait against the shared index.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:02:00")]
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The derived collection name, <c>{env}_jobhunter_jobs</c> (data-model §Typesense collection). Never
    /// set directly, so the helios naming convention is structural.
    /// </summary>
    public string CollectionName => $"{EnvironmentPrefix}_jobhunter_jobs";
}
