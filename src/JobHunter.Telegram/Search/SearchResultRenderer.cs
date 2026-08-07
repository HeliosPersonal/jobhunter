using System.Globalization;
using System.Text;
using JobHunter.Domain.Search;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Search;

/// <summary>
/// Renders <see cref="SearchResults"/> in the digest card layout (F5 message contract §Card, AC-11) so the
/// bot's <c>/search</c> speaks the same visual language as the morning digest. It shares the API's query
/// service and only this renderer differs (the O12 decision). Every dynamic value passes through
/// <see cref="MarkdownV2Escaper"/> — there is no path to message text that interpolates an unescaped value
/// (the F5 escaping rule, enforced there by an architecture test).
///
/// <para>Three DoD behaviours live here: no results produces a helpful "broaden your query" message rather
/// than an empty response; results are capped at ten with the total <c>found</c> count so the Owner knows
/// there is more; and the F4-owned score is only shown once it is a real ranking (a zero score is
/// un-ranked until F4 merges and is omitted, never shown as "0", the cross-feature decoupling decision).</para>
/// </summary>
internal static class SearchResultRenderer
{
    private const int MaxTitleLength = 60;

    // Maps the F9 facet field names the index returns onto the catalogue's own filter keys, so a rendered
    // facet is a token the Owner can paste straight back into the next /search. Only the three fields the
    // catalogue grammar can express are surfaced; the order here is the order they are shown.
    private static readonly (string Field, string FilterKey)[] RefinableFacets =
    [
        ("technologies", "tech"),
        ("companyStage", "stage"),
        ("countries", "country"),
    ];

    public static string Render(string rawQuery, SearchResults results, SearchQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Hits.Count == 0)
        {
            return RenderEmpty(rawQuery, query);
        }

        var shown = Math.Min(results.Hits.Count, SearchCommandParser.ResultLimit);
        var builder = new StringBuilder();

        builder.Append('*').Append(MarkdownV2Escaper.Escape(FormatFound(results.Found, shown))).Append("*\n");

        for (var i = 0; i < shown; i++)
        {
            builder.Append('\n');
            AppendCard(builder, results.Hits[i].Document);
        }

        AppendFacets(builder, results.Facets);

        if (results.Partial)
        {
            // The provider degraded under load; say so rather than present a short page as complete (QG-3).
            builder.Append("\n_").Append(MarkdownV2Escaper.Escape(
                "Partial results — the index was busy; try again for the full set.")).Append("_\n");
        }

        return builder.ToString();
    }

    private static void AppendFacets(
        StringBuilder builder, IReadOnlyDictionary<string, IReadOnlyList<FacetCount>> facets)
    {
        if (facets.Count == 0)
        {
            return;
        }

        // The leading value of each refinable facet, ordered by count, becomes a paste-back filter token so the
        // next query can be narrower with no second round trip (AC-02).
        var tokens = new List<string>();
        foreach (var (field, filterKey) in RefinableFacets)
        {
            if (facets.TryGetValue(field, out var counts) && counts.Count > 0)
            {
                var top = counts.MaxBy(c => c.Count)!;
                if (!string.IsNullOrWhiteSpace(top.Value))
                {
                    tokens.Add($"{filterKey}:{top.Value}");
                }
            }
        }

        if (tokens.Count == 0)
        {
            return;
        }

        builder.Append("\n_")
            .Append(MarkdownV2Escaper.Escape("Refine: " + string.Join(" · ", tokens)))
            .Append("_\n");
    }

    private static string RenderEmpty(string rawQuery, SearchQuery? query)
    {
        var trimmed = (rawQuery ?? string.Empty).Trim();

        // With active filters, the most restrictive one is the reason the set is empty; name it to drop rather
        // than the generic "broaden" advice, so the next query is one edit away from results (catalogue DoD).
        if (query is not null && MostRestrictiveFilter(query) is { } toDrop)
        {
            var line = string.IsNullOrEmpty(trimmed)
                ? $"No results. Try dropping the most restrictive filter, {toDrop}."
                : $"No results for \"{trimmed}\". Try dropping the most restrictive filter, {toDrop}.";
            return "_" + MarkdownV2Escaper.Escape(line) + "_";
        }

        var broaden = string.IsNullOrEmpty(trimmed)
            ? "No results. Try a broader query, for example: /search staff backend remote:remote."
            : $"No results for \"{trimmed}\". Try fewer filters or a broader query.";
        return "_" + MarkdownV2Escaper.Escape(broaden) + "_";
    }

    /// <summary>
    /// The catalogue token for the single filter that most narrows a query — a numeric threshold cuts hardest,
    /// then a date window, then the typed sets in catalogue order — or null when the query carries no filter
    /// at all (a pure free-text search, where "drop a filter" is not the right advice).
    /// </summary>
    private static string? MostRestrictiveFilter(SearchQuery query)
    {
        if (query.MinScore is not null)
        {
            return "min:";
        }

        if (query.SalaryMin is not null)
        {
            return "min-salary:";
        }

        if (query.PostedAfter is not null)
        {
            return "since:";
        }

        if (query.Technologies.Count > 0)
        {
            return "tech:";
        }

        if (query.CompanyStages.Count > 0)
        {
            return "stage:";
        }

        if (query.Countries.Count > 0)
        {
            return "country:";
        }

        if (query.RemotePolicies.Count > 0)
        {
            return "remote:";
        }

        return query.Seniorities.Count > 0 ? "seniority:" : null;
    }

    private static void AppendCard(StringBuilder builder, JobDocument doc)
    {
        builder.Append('*').Append(MarkdownV2Escaper.Escape(TruncateTitle(doc.Title))).Append("*\n");

        var companyLine = BuildCompanyLine(doc);
        builder.Append(MarkdownV2Escaper.Escape(companyLine)).Append('\n');

        var salaryLine = BuildSalaryLine(doc);
        if (salaryLine is not null)
        {
            builder.Append(MarkdownV2Escaper.Escape(salaryLine));
        }

        // The score is F4's explainability guarantee; a 0 means un-ranked (F4 not merged), so it is omitted
        // rather than shown as a real ranking of zero (the decoupling decision).
        if (doc.Score > 0d)
        {
            if (salaryLine is not null)
            {
                builder.Append(MarkdownV2Escaper.Escape(" · "));
            }

            builder.Append(MarkdownV2Escaper.Escape("🎯 "))
                .Append('*')
                .Append(MarkdownV2Escaper.Escape(Math.Round(doc.Score).ToString("0", CultureInfo.InvariantCulture)))
                .Append('*');
        }

        if (salaryLine is not null || doc.Score > 0d)
        {
            builder.Append('\n');
        }
    }

    private static string BuildCompanyLine(JobDocument doc)
    {
        var parts = new List<string> { doc.CompanyName };
        if (!string.IsNullOrWhiteSpace(doc.CompanyStage))
        {
            parts.Add(doc.CompanyStage);
        }

        var location = doc.Countries.Count > 0 ? string.Join(" / ", doc.Countries) : doc.RemotePolicy;
        parts.Add(location);
        return string.Join(" · ", parts);
    }

    private static string? BuildSalaryLine(JobDocument doc)
    {
        if (doc.SalaryMin is not { } min || doc.SalaryMax is not { } max)
        {
            return null;
        }

        var currency = string.IsNullOrWhiteSpace(doc.SalaryCurrency) ? string.Empty : " " + doc.SalaryCurrency;
        return $"💰 {FormatThousands(min)}–{FormatThousands(max)}{currency}";
    }

    private static string FormatThousands(int amount) =>
        amount >= 1000
            ? (amount / 1000).ToString("0", CultureInfo.InvariantCulture) + "k"
            : amount.ToString("0", CultureInfo.InvariantCulture);

    private static string FormatFound(int found, int shown) =>
        found <= shown
            ? $"{found} result{(found == 1 ? string.Empty : "s")}"
            : $"Showing {shown} of {found} results";

    private static string TruncateTitle(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(title);
        var elements = new List<string>();
        while (enumerator.MoveNext())
        {
            elements.Add((string)enumerator.Current);
        }

        if (elements.Count <= MaxTitleLength)
        {
            return title;
        }

        // Truncate on graphemes, then back off to the last word boundary so a multi-byte script or an emoji
        // is never split (the F5 rendering-corpus rule).
        var clipped = string.Concat(elements.Take(MaxTitleLength));
        var lastSpace = clipped.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            clipped = clipped[..lastSpace];
        }

        return clipped + "…";
    }
}
