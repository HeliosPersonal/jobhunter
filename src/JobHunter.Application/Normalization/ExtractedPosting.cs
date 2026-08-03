using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// The provider-agnostic fields an <see cref="IPostingNormalizer"/> extracts from one raw payload before
/// the shared normalisers run (SAD §5, §6.1). It is the seam between provider-specific parsing and the
/// pipeline: every provider produces this same shape, so five providers never become five copies of the
/// title/location/salary logic. It carries what the provider actually published — nothing is inferred
/// here. <see cref="Title"/> and <see cref="ApplyUrl"/> are the two required fields; a null in either is a
/// normalisation failure (AC-04), never a job with an empty title.
///
/// <para>Signals the shared normalisers need are passed through verbatim: <see cref="LocationText"/> is the
/// free-text location a provider gives when it has no structured parts; <see cref="RemoteSignal"/> is an
/// explicit provider workplace type when one exists (Lever, Ashby), else null so the resolver infers from
/// the location text (never from the description).</para>
/// </summary>
public sealed record ExtractedPosting
{
    /// <summary>The published title, exactly as given (never blank on success). Null is a failure.</summary>
    public string? Title { get; init; }

    /// <summary>The apply URL, as published (never blank on success). Null is a failure.</summary>
    public string? ApplyUrl { get; init; }

    /// <summary>Plain-text description; empty is legal (a job with no description is still a job).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>An already-structured location set, when the provider gives country/region/city parts.</summary>
    public LocationSet? Locations { get; init; }

    /// <summary>Free-text location, when the provider gives only a blob ("Remote - EMEA").</summary>
    public string? LocationText { get; init; }

    /// <summary>An explicit provider remote signal that wins over inference, or null to infer from text.</summary>
    public RemotePolicy? RemoteSignal { get; init; }

    /// <summary>The engagement, defaulting to <see cref="EmploymentType.Unknown"/> when unstated.</summary>
    public EmploymentType EmploymentType { get; init; } = EmploymentType.Unknown;

    /// <summary>The raw salary text to parse, or null when the provider published none.</summary>
    public string? SalaryText { get; init; }

    /// <summary>The default period for a salary with no stated period (annual is the ATS default).</summary>
    public SalaryPeriod SalaryDefaultPeriod { get; init; } = SalaryPeriod.Year;

    /// <summary>When the posting was published, or null when the provider did not say.</summary>
    public DateTimeOffset? PostedAt { get; init; }

    /// <summary>The precision of <see cref="PostedAt"/> — some providers publish a date only.</summary>
    public PostedAtGranularity PostedAtGranularity { get; init; } = PostedAtGranularity.Exact;

    /// <summary>True for a JSON-LD career-page origin — the lowest-confidence Tier-2 binding.</summary>
    public bool IsTier2 { get; init; }
}
