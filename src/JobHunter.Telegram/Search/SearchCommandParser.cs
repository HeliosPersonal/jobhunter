using System.Globalization;
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
        ["min"] = Filter.MinScore,
        ["closed"] = Filter.IncludeClosed,
        ["since"] = Filter.PostedAfter,
    };

    // The relative-window units the catalogue's `since:` accepts, each mapped to its length in seconds.
    // Anything else (a "30y", a bare number) falls through to free text — a malformed value never errors.
    private static readonly Dictionary<char, long> SinceUnits = new()
    {
        ['h'] = 3_600L,
        ['d'] = 86_400L,
        ['w'] = 7L * 86_400L,
    };

    // The values the catalogue's `closed:` filter accepts to opt closed jobs back in. Anything else falls
    // through to free text rather than erroring — the argument-parsing rule (a malformed value never errors).
    private static readonly HashSet<string> ClosedYesValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "true", "1",
    };

    public static SearchQuery Parse(string? arguments, DateTimeOffset? now = null)
    {
        var technologies = new List<string>();
        var remotePolicies = new List<string>();
        var countries = new List<string>();
        var seniorities = new List<string>();
        var stages = new List<string>();
        var textTerms = new List<string>();
        int? salaryMin = null;
        double? minScore = null;
        var includeClosed = false;
        long? postedAfter = null;

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
                    case Filter.MinScore:
                        // The minimum score (catalogue `min:`), distinct from `min-salary:`. A non-numeric
                        // value falls through to free text, like every other malformed filter.
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
                        {
                            minScore = score;
                        }
                        else
                        {
                            textTerms.Add(token);
                        }

                        break;
                    case Filter.IncludeClosed:
                        // `closed:yes` (or true/1) opts closed jobs back in; anything else is free text, so
                        // "closed:maybe" is a search term rather than an error (AC-08, default excludes closed).
                        if (ClosedYesValues.Contains(value))
                        {
                            includeClosed = true;
                        }
                        else
                        {
                            textTerms.Add(token);
                        }

                        break;
                    case Filter.PostedAfter:
                        // `since:30d` is a relative window resolved against "now" to an absolute unix-second
                        // cutoff. Without a clock (the pure overload) or with a value that is not a positive
                        // number followed by a known unit, it falls through to free text like any other.
                        if (now is { } instant && TryResolveSince(value, instant, out var cutoff))
                        {
                            postedAfter = cutoff;
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
            MinScore = minScore,
            IncludeClosed = includeClosed,
            PostedAfter = postedAfter,
            Limit = ResultLimit,
        };
    }

    /// <summary>
    /// Resolves a relative window like <c>30d</c> to an absolute unix-second cutoff: a positive integer
    /// followed by a single unit character (<c>h</c>/<c>d</c>/<c>w</c>). Returns false — leaving the token as
    /// free text — for any other shape, so a fat-fingered <c>since:</c> never fails the whole search.
    /// </summary>
    private static bool TryResolveSince(string value, DateTimeOffset now, out long cutoff)
    {
        cutoff = 0;
        if (value.Length < 2)
        {
            return false;
        }

        var unit = value[^1];
        if (!SinceUnits.TryGetValue(char.ToLowerInvariant(unit), out var unitSeconds)
            || !long.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0)
        {
            return false;
        }

        cutoff = now.ToUnixTimeSeconds() - (amount * unitSeconds);
        return true;
    }

    private enum Filter
    {
        Technology,
        RemotePolicy,
        Country,
        Seniority,
        CompanyStage,
        SalaryMin,
        MinScore,
        IncludeClosed,
        PostedAfter,
    }
}
