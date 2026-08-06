using System.Text.Json;

namespace JobHunter.Infrastructure.Persistence.Pipeline;

/// <summary>
/// The one serialisation of a Guid list to and from a <c>jsonb</c> array column — used for
/// <c>digest_cards.grouped_job_ids</c>, the near-duplicate jobs a card groups away (F5-T13, data-model
/// §digest_cards). A null or empty column round-trips to an empty list; the "grouped, never dropped" property
/// lives in the <see cref="JobHunter.Domain.Reporting.DigestCard"/> aggregate, not here.
/// </summary>
internal static class GuidListJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<Guid> values) =>
        JsonSerializer.Serialize(values, Options);

    public static List<Guid> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<Guid>>(json, Options) ?? [];
}
