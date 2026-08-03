using System.Globalization;
using JobHunter.Domain.Search;
using Microsoft.AspNetCore.Http;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// Binds the <c>GET /api/search</c> query string into a typed <see cref="SearchQuery"/> (API contract
/// §Search). The binding is deliberately its own pure function of the query collection: a user term
/// becomes a typed parameter and never a filter operator — the filter expression is built downstream from
/// these typed values by the query service, which is what makes filter-injection a non-issue (AC-02). An
/// unparseable number or flag is simply dropped to its default rather than failing the request, so a
/// stray <c>minScore=abc</c> degrades to "no minimum" instead of a 500.
/// </summary>
internal static class SearchQueryBinding
{
    public static SearchQuery FromQuery(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new SearchQuery
        {
            Text = query["q"].ToString(),
            Technologies = Multi(query, "technology"),
            CompanyStages = Multi(query, "companyStage"),
            RemotePolicies = Multi(query, "remotePolicy"),
            Countries = Multi(query, "country"),
            Seniorities = Multi(query, "seniority"),
            MinScore = OptionalDouble(query, "minScore"),
            SalaryMin = OptionalInt(query, "salaryMin"),
            IncludeClosed = OptionalBool(query, "includeClosed"),
            Limit = OptionalInt(query, "limit") ?? 20,
            Cursor = OptionalString(query, "cursor"),
        };
    }

    private static IReadOnlyList<string> Multi(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var values)
            ? [.. values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())]
            : [];

    private static string? OptionalString(IQueryCollection query, string key)
    {
        var value = query[key].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static double? OptionalDouble(IQueryCollection query, string key) =>
        double.TryParse(query[key].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static int? OptionalInt(IQueryCollection query, string key) =>
        int.TryParse(query[key].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool OptionalBool(IQueryCollection query, string key) =>
        bool.TryParse(query[key].ToString(), out var parsed) && parsed;
}
