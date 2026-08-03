using System.Globalization;
using System.Net;
using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Search;

/// <summary>
/// The Typesense adapter behind <see cref="ISearchQuery"/> (SAD §6.2, F9-T03). It turns a typed
/// <see cref="SearchQuery"/> into a Typesense search request and the response back into
/// <see cref="SearchResults"/>. Three rules are load-bearing:
/// <list type="bullet">
///   <item>the <c>filter_by</c> expression is built from typed parameters by
///   <see cref="SearchFilterBuilder"/> and every user term is escaped — user input never becomes a
///   filter operator (AC-02);</item>
///   <item>closed jobs are excluded unless <see cref="SearchQuery.IncludeClosed"/> is set (AC-08);</item>
///   <item>an unreachable or erroring index is a <see cref="Result{T}"/> failure, never a partial page
///   presented as complete and never an exception (AC-09, QG-3).</item>
/// </list>
/// Typo tolerance is Typesense's default (one to two typos by term length), which is what returns the
/// intended match for a misspelled technology (AC-03) — the service asks for it, it is not re-implemented
/// here.
/// </summary>
public sealed class TypesenseQueryService : ISearchQuery
{
    /// <summary>The named <see cref="HttpClient"/> shared with the indexer — one Typesense configuration.</summary>
    public const string HttpClientName = TypesenseIndexer.HttpClientName;

    /// <summary>Page size is clamped to this ceiling so a single request cannot ask for an unbounded scan.</summary>
    public const int MaxLimit = 100;

    /// <summary>Free text longer than this is truncated at a word boundary rather than rejected (edge cases).</summary>
    public const int MaxQueryLength = 500;

    /// <summary>Facet values per field are capped at the top values by count (edge cases).</summary>
    private const int MaxFacetValues = 20;

    /// <summary>The fields a full-text query searches; the id, enums and numerics are filtered, not searched.</summary>
    private const string QueryByFields = "title,companyName,technologies,description";

    private static readonly Error Unavailable =
        new("search.unavailable", "The search index is unavailable.");

    private static readonly Error InvalidCursor =
        new("search.cursor.invalid", "The pagination cursor is not valid.");

    private readonly HttpClient _http;
    private readonly TypesenseOptions _options;
    private readonly ILogger<TypesenseQueryService> _logger;

    public TypesenseQueryService(
        HttpClient http,
        IOptions<TypesenseOptions> options,
        ILogger<TypesenseQueryService> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<SearchResults>> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var buildResult = BuildRequestUri(query);
        if (buildResult.IsFailure)
        {
            return Result<SearchResults>.Failure(buildResult.Error);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, buildResult.Value);
        request.Headers.TryAddWithoutValidation("X-TYPESENSE-API-KEY", _options.ApiKey);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // A bad request from a malformed filter is the caller's fault; anything else is an
                // unavailable index. Either way it is a value, never a partial result dressed as complete.
                _logger.LogWarning(
                    "Typesense search returned {StatusCode}; reporting search as unavailable.",
                    (int)response.StatusCode);
                return Result<SearchResults>.Failure(Unavailable);
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseResults(payload, query.Limit);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Typesense search could not reach the index.");
            return Result<SearchResults>.Failure(Unavailable);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Typesense search timed out.");
            return Result<SearchResults>.Failure(Unavailable);
        }
    }

    private Result<string> BuildRequestUri(SearchQuery query)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 20 : query.Limit, 1, MaxLimit);
        var text = Truncate(query.Text);

        var filter = SearchFilterBuilder.Build(query);
        if (query.Cursor is not null)
        {
            if (!SearchCursor.TryDecode(query.Cursor, out var position))
            {
                return Result<string>.Failure(InvalidCursor);
            }

            // Keyset paging on the score sort: the next page is everything ranked strictly below the last
            // hit seen. Typesense cannot range-filter the string id, so exact-score ties that straddle a
            // page boundary are the one accepted edge — real ranking scores are near-unique and the tail
            // where this bites (the unranked score-0 cluster) is the lowest-priority end of the corpus.
            var boundary = $"score:<{position.Score.ToString(CultureInfo.InvariantCulture)}";
            filter = filter is null ? boundary : $"{filter} && {boundary}";
        }

        var parameters = new List<string>
        {
            // An empty query is legal (filters-only search): Typesense treats "*" as match-all.
            $"q={Uri.EscapeDataString(string.IsNullOrEmpty(text) ? "*" : text)}",
            $"query_by={Uri.EscapeDataString(QueryByFields)}",
            "sort_by=score:desc",
            $"per_page={limit.ToString(CultureInfo.InvariantCulture)}",
            $"facet_by={Uri.EscapeDataString(string.Join(",", SearchSchema.FacetFields))}",
            $"max_facet_values={MaxFacetValues.ToString(CultureInfo.InvariantCulture)}",
        };

        if (filter is not null)
        {
            parameters.Add($"filter_by={Uri.EscapeDataString(filter)}");
        }

        return Result<string>.Success(
            $"collections/{_options.CollectionName}/documents/search?{string.Join("&", parameters)}");
    }

    private static Result<SearchResults> ParseResults(string payload, int limit)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Result<SearchResults>.Failure(Unavailable);
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var hits = ReadHits(root);
            var found = root.TryGetProperty("found", out var foundEl) ? foundEl.GetInt32() : hits.Count;
            var facets = ReadFacets(root);

            // A page that came back full may have more behind it; the cursor is the last hit's position.
            string? nextCursor = null;
            if (hits.Count >= limit && hits.Count > 0)
            {
                var last = hits[^1].Document;
                nextCursor = SearchCursor.Encode(last.Score, last.Id);
            }

            // Typesense sets this when it returned an approximate/partial result under load — reported
            // as-is, never silently truncated (edge cases).
            var partial = root.TryGetProperty("search_cutoff", out var cutoff)
                && cutoff.ValueKind == JsonValueKind.True;

            return Result<SearchResults>.Success(new SearchResults(hits, found, facets, nextCursor, partial));
        }
        catch (JsonException)
        {
            return Result<SearchResults>.Failure(Unavailable);
        }
    }

    private static List<SearchHit> ReadHits(JsonElement root)
    {
        if (!root.TryGetProperty("hits", out var hitsEl) || hitsEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hits = new List<SearchHit>(hitsEl.GetArrayLength());
        foreach (var hit in hitsEl.EnumerateArray())
        {
            if (!hit.TryGetProperty("document", out var doc))
            {
                continue;
            }

            hits.Add(new SearchHit(TypesenseSerialization.ReadDocument(doc), ReadHighlight(hit)));
        }

        return hits;
    }

    private static string? ReadHighlight(JsonElement hit)
    {
        if (!hit.TryGetProperty("highlights", out var highlights)
            || highlights.ValueKind != JsonValueKind.Array
            || highlights.GetArrayLength() == 0)
        {
            return null;
        }

        var first = highlights[0];
        return first.TryGetProperty("snippet", out var snippet) ? snippet.GetString() : null;
    }

    private static Dictionary<string, IReadOnlyList<FacetCount>> ReadFacets(JsonElement root)
    {
        var facets = new Dictionary<string, IReadOnlyList<FacetCount>>(StringComparer.Ordinal);
        if (!root.TryGetProperty("facet_counts", out var facetCounts)
            || facetCounts.ValueKind != JsonValueKind.Array)
        {
            return facets;
        }

        foreach (var facet in facetCounts.EnumerateArray())
        {
            if (!facet.TryGetProperty("field_name", out var fieldEl) || fieldEl.GetString() is not { } field)
            {
                continue;
            }

            var counts = new List<FacetCount>();
            if (facet.TryGetProperty("counts", out var countsEl) && countsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var count in countsEl.EnumerateArray())
                {
                    var value = count.TryGetProperty("value", out var v) ? v.GetString() : null;
                    var number = count.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
                    if (value is not null)
                    {
                        counts.Add(new FacetCount(value, number));
                    }
                }
            }

            facets[field] = counts;
        }

        return facets;
    }

    private static string Truncate(string text)
    {
        if (text.Length <= MaxQueryLength)
        {
            return text;
        }

        // Cut at the last word boundary before the ceiling so a truncated query stays sensible (edge cases).
        var slice = text[..MaxQueryLength];
        var lastSpace = slice.LastIndexOf(' ');
        return lastSpace > 0 ? slice[..lastSpace] : slice;
    }
}
