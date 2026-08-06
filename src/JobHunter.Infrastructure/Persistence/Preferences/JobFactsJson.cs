using System.Text.Json;
using JobHunter.Domain.Preferences;

namespace JobHunter.Infrastructure.Persistence.Preferences;

/// <summary>
/// The one serialisation of a <see cref="JobFacts"/> snapshot to and from the <c>signals.job_facts</c>
/// <c>jsonb</c> column (F7 data-model §signals). Facts travel as a dimension-keyed map of value lists
/// (<c>{"Country": ["DE"], "Technology": ["Kafka", "Go"]}</c>). Rehydration goes back through
/// <see cref="JobFacts.Create"/>, so the "non-empty, trimmed, deduplicated" invariant lives in the value
/// object rather than being trusted from storage.
/// </summary>
internal static class JobFactsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(JobFacts facts)
    {
        var map = facts.Dimensions.ToDictionary(
            d => d.ToString(),
            d => facts.ValuesFor(d));
        return JsonSerializer.Serialize(map, Options);
    }

    public static JobFacts Deserialize(string json)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, Options) ?? [];
        var facts = new Dictionary<Dimension, IReadOnlyList<string>>();
        foreach (var (key, values) in map)
        {
            if (Enum.TryParse<Dimension>(key, out var dimension))
            {
                facts[dimension] = values;
            }
        }

        return JobFacts.Create(facts);
    }
}
