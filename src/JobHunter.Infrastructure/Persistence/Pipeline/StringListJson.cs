using System.Text.Json;

namespace JobHunter.Infrastructure.Persistence.Pipeline;

/// <summary>
/// The one serialisation of a string list to and from a <c>jsonb</c> array column — used for
/// <c>enrichments.reasons</c> and <c>enrichments.technologies</c> (data-model §enrichments). A null or
/// empty column round-trips to an empty list; the non-empty-reasons invariant is enforced by the
/// <see cref="JobHunter.Domain.Intelligence.Enrichment"/> constructor, not here (invariant 4).
/// </summary>
internal static class StringListJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<string> values) =>
        JsonSerializer.Serialize(values, Options);

    public static List<string> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];
}
