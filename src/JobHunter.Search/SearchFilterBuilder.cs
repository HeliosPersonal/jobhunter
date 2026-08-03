using System.Globalization;
using System.Text;
using JobHunter.Domain.Search;

namespace JobHunter.Search;

/// <summary>
/// Builds a Typesense <c>filter_by</c> expression from the <strong>typed</strong> <see cref="SearchQuery"/>
/// parameters (SAD §8, T03 AC-02). User input is never concatenated into the expression: every supplied
/// term is a value in a <c>field:=[`term`]</c> clause with the term backtick-escaped, so a term that
/// itself contains Typesense filter syntax (<c>&amp;&amp;</c>, <c>:=</c>, a stray backtick) is matched as
/// literal text and can never change the shape of the filter. That is the whole of the injection defence —
/// there is no code path from a request string to a filter operator.
/// </summary>
internal static class SearchFilterBuilder
{
    /// <summary>
    /// The default clause that keeps closed jobs out of every result unless the caller explicitly asks for
    /// them (AC-08). Status is an internal enum value, never user input, so it needs no escaping.
    /// </summary>
    private const string LiveOnly = "status:=`Live`";

    /// <summary>
    /// Composes the full <c>filter_by</c> string, or null when there is nothing to filter (a bare
    /// full-text query over every live job). The clauses are joined with <c>&amp;&amp;</c>; within a
    /// multi-value field the values are an OR set, which is how a client refines "Kafka or Azure".
    /// </summary>
    public static string? Build(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var clauses = new List<string>();

        if (!query.IncludeClosed)
        {
            clauses.Add(LiveOnly);
        }

        AddStringSet(clauses, "technologies", query.Technologies);
        AddStringSet(clauses, "companyStage", query.CompanyStages);
        AddStringSet(clauses, "remotePolicy", query.RemotePolicies);
        AddStringSet(clauses, "countries", query.Countries);
        AddStringSet(clauses, "seniority", query.Seniorities);

        if (query.MinScore is { } minScore)
        {
            clauses.Add($"score:>={minScore.ToString(CultureInfo.InvariantCulture)}");
        }

        if (query.SalaryMin is { } salaryMin)
        {
            clauses.Add($"salaryMin:>={salaryMin.ToString(CultureInfo.InvariantCulture)}");
        }

        return clauses.Count == 0 ? null : string.Join(" && ", clauses);
    }

    private static void AddStringSet(List<string> clauses, string field, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        var escaped = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(Escape)
            .ToList();

        if (escaped.Count == 0)
        {
            return;
        }

        clauses.Add($"{field}:=[{string.Join(",", escaped)}]");
    }

    /// <summary>
    /// Wraps a value in backticks — Typesense's literal-value delimiter — with any embedded backtick
    /// removed so the delimiter itself cannot be broken out of. The result is always exactly one balanced
    /// backtick-quoted token, whatever the input contained.
    /// </summary>
    private static string Escape(string value)
    {
        var sanitised = value.Replace("`", string.Empty, StringComparison.Ordinal);
        return new StringBuilder(sanitised.Length + 2).Append('`').Append(sanitised).Append('`').ToString();
    }
}
