namespace JobHunter.Domain.Search;

/// <summary>
/// A search request expressed in <strong>typed parameters</strong> (SAD §8, T03). The query service
/// builds the provider filter expression from these — user input is never concatenated into a filter,
/// which is what makes filter-injection a non-issue (AC-02). Every collection defaults to empty, so a
/// bare full-text query is valid and an empty <see cref="Text"/> with filters only is valid too
/// (test-plan §edge cases).
/// </summary>
public sealed record SearchQuery
{
    /// <summary>The free-text query; empty is legal (filters-only search).</summary>
    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<string> Technologies { get; init; } = [];

    public IReadOnlyList<string> CompanyStages { get; init; } = [];

    public IReadOnlyList<string> RemotePolicies { get; init; } = [];

    public IReadOnlyList<string> Countries { get; init; } = [];

    public IReadOnlyList<string> Seniorities { get; init; } = [];

    public double? MinScore { get; init; }

    public int? SalaryMin { get; init; }

    /// <summary>Closed jobs are excluded unless this is true (AC-08).</summary>
    public bool IncludeClosed { get; init; }

    /// <summary>
    /// Narrows to jobs first posted at or after this instant, as unix seconds. The catalogue expresses this
    /// as a relative <c>since:30d</c>, which the command surface resolves to an absolute cutoff before the
    /// query is built — the domain only ever sees the instant.
    /// </summary>
    public long? PostedAfter { get; init; }

    /// <summary>Page size; the service clamps it to a sane maximum.</summary>
    public int Limit { get; init; } = 20;

    /// <summary>An opaque cursor from a previous page, or null for the first page.</summary>
    public string? Cursor { get; init; }
}
