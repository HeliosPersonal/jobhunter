using JobHunter.Domain.Search;

namespace JobHunter.Telegram.Search;

/// <summary>
/// Turns the raw argument string of <c>/search &lt;query&gt;</c> into a typed <see cref="SearchQuery"/>
/// (F9-T09). Filters can be expressed inline in a small, documented syntax — <c>key:value</c> tokens
/// anywhere in the line — and everything that is not a recognised filter token is the free-text query, so
/// <c>staff sre remote:remote country:Germany</c> searches "staff sre" filtered to remote roles in
/// Germany. Parsing is total: an unknown <c>key:value</c> token is treated as free text rather than
/// rejected, so a colon in an ordinary query term never turns into an error.
///
/// <para>The command shares the API's <see cref="ISearchQuery"/> port and only the renderer differs (the
/// O12 decision), so the filter vocabulary here is deliberately the same typed parameters the API binds —
/// the query is built from typed values, never by concatenating user input into a filter expression
/// (AC-02).</para>
/// </summary>
internal static class SearchCommandParser
{
    /// <summary>The card cap for the bot surface — ten results with a total-found count (AC-11, DoD).</summary>
    public const int ResultLimit = 10;

    // Multi-value filters collect every occurrence; a repeated key widens the set (tech:go tech:rust).
    private static readonly Dictionary<string, Filter> Filters = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["tech"] = Filter.Technology,
        ["remote"] = Filter.RemotePolicy,
        ["country"] = Filter.Country,
        ["seniority"] = Filter.Seniority,
        ["stage"] = Filter.CompanyStage,
        ["min-salary"] = Filter.SalaryMin,
    };

    public static SearchQuery Parse(string? arguments)
    {
        var technologies = new List<string>();
        var remotePolicies = new List<string>();
        var countries = new List<string>();
        var seniorities = new List<string>();
        var stages = new List<string>();
        var textTerms = new List<string>();
        int? salaryMin = null;

        var tokens = (arguments ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var colon = token.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && colon < token.Length - 1
                && Filters.TryGetValue(token[..colon], out var filter))
            {
                var value = token[(colon + 1)..];
                switch (filter)
                {
                    case Filter.Technology: technologies.Add(value); break;
                    case Filter.RemotePolicy: remotePolicies.Add(value); break;
                    case Filter.Country: countries.Add(value); break;
                    case Filter.Seniority: seniorities.Add(value); break;
                    case Filter.CompanyStage: stages.Add(value); break;
                    case Filter.SalaryMin:
                        // A non-numeric min-salary is not an error — it falls through to free text, so a
                        // fat-fingered value never turns the whole search into a failure.
                        if (int.TryParse(value, out var parsed))
                        {
                            salaryMin = parsed;
                        }
                        else
                        {
                            textTerms.Add(token);
                        }

                        break;
                }

                continue;
            }

            textTerms.Add(token);
        }

        return new SearchQuery
        {
            Text = string.Join(' ', textTerms),
            Technologies = technologies,
            RemotePolicies = remotePolicies,
            Countries = countries,
            Seniorities = seniorities,
            CompanyStages = stages,
            SalaryMin = salaryMin,
            Limit = ResultLimit,
        };
    }

    private enum Filter
    {
        Technology,
        RemotePolicy,
        Country,
        Seniority,
        CompanyStage,
        SalaryMin,
    }
}
