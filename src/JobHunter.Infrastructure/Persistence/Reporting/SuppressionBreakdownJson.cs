using System.Text.Json;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Reporting;

/// <summary>
/// The one serialisation of a <see cref="SuppressionTally"/> list to and from the
/// <c>digests.suppression_breakdown</c> <c>jsonb</c> column (data-model §digests). Each tally travels as
/// <c>{reason, count}</c>. Rehydration goes back through <see cref="SuppressionTally.TryCreate"/>, so a
/// stored blank reason or negative count is dropped rather than trusted — the invariant lives in the value
/// object, not here. A null or empty column round-trips to an empty list.
/// </summary>
internal static class SuppressionBreakdownJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private sealed record Row(string Reason, int Count);

    public static string Serialize(IReadOnlyList<SuppressionTally> tallies) =>
        JsonSerializer.Serialize(tallies.Select(t => new Row(t.Reason, t.Count)), Options);

    public static List<SuppressionTally> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var rows = JsonSerializer.Deserialize<List<Row>>(json, Options) ?? [];
        return rows
            .Select(r => SuppressionTally.TryCreate(r.Reason, r.Count))
            .Where(result => result.IsSuccess)
            .Select(result => result.Value)
            .ToList();
    }
}
