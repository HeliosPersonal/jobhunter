namespace JobHunter.Domain.Search;

/// <summary>
/// One hit in a search result: the indexed document plus an optional match highlight snippet. The
/// document is the allowlisted <see cref="JobDocument"/>, so a hit can never carry more than the index
/// does (QG-2).
/// </summary>
public sealed record SearchHit(JobDocument Document, string? Highlight);

/// <summary>One facet value and how many documents in the result carry it (AC-02).</summary>
public sealed record FacetCount(string Value, int Count);

/// <summary>
/// The result of a search (API contract §Search): the hits, the total <see cref="Found"/> count, the
/// facet counts per faceted field so a client can offer refinements with no second round trip, the next
/// <see cref="NextCursor"/> (null at the end), and a <see cref="Partial"/> flag set when the provider
/// returned a degraded/partial result under load — reported as-is, never silently truncated (test-plan
/// §edge cases).
/// </summary>
public sealed record SearchResults(
    IReadOnlyList<SearchHit> Hits,
    int Found,
    IReadOnlyDictionary<string, IReadOnlyList<FacetCount>> Facets,
    string? NextCursor,
    bool Partial);
