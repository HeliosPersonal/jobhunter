namespace JobHunter.Domain.Search;

/// <summary>
/// The read-side inputs a <see cref="JobDocument"/> is projected from — a flat, provider-agnostic view of
/// the <see cref="Jobs.Job"/> aggregate joined to its <see cref="Companies.Company"/> (data-model
/// §Projection). It is deliberately a separate record rather than the aggregate itself: the projection is
/// a pure function of <em>exactly these</em> fields, so what can reach the index is bounded by this type,
/// and the enrichment/score/application inputs that F3/F4/F6 own arrive as nullable members populated as
/// null until those features merge (per the F9 decoupling decision).
/// </summary>
public sealed record JobProjectionSource
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string Status { get; init; }

    public required string CompanyName { get; init; }

    public required string CompanyDomain { get; init; }

    /// <summary>Set by F3, null until it lands.</summary>
    public string? CompanyStage { get; init; }

    /// <summary>The deterministic vocabulary tags (F2) unioned with any inferred set once F3 lands.</summary>
    public IReadOnlyList<string> Technologies { get; init; } = [];

    /// <summary>The distinct countries from the job's locations (F2); empty for a fully-remote job.</summary>
    public IReadOnlyList<string> Countries { get; init; } = [];

    public required string RemotePolicy { get; init; }

    public string? Seniority { get; init; }

    public required string EmploymentType { get; init; }

    /// <summary>Set by F3 enrichment, null until it lands.</summary>
    public string? AiUsage { get; init; }

    public int? SalaryMin { get; init; }

    public int? SalaryMax { get; init; }

    public string? SalaryCurrency { get; init; }

    /// <summary>The latest final score (F4); null until F4 lands, projected as 0.</summary>
    public double? Score { get; init; }

    public DateTimeOffset? PostedAt { get; init; }

    public required DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>The application status (F6), null when none or until F6 lands.</summary>
    public string? ApplicationStatus { get; init; }
}
