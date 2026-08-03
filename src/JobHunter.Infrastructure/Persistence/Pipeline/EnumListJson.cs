using System.Text.Json;

namespace JobHunter.Infrastructure.Persistence.Pipeline;

/// <summary>
/// Serialises a list of enum values to and from a <c>jsonb</c> array of <em>names</em> — never ordinals
/// (coding-standards §5) — used for <c>profiles.employment_types</c> (data-model §profiles). A null or
/// empty column round-trips to an empty list. Storing the names keeps the column readable and immune to
/// an enum being reordered, exactly as the global enum-as-text convention requires for scalar columns.
/// </summary>
internal static class EnumListJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<TEnum>(IReadOnlyList<TEnum> values)
        where TEnum : struct, Enum =>
        JsonSerializer.Serialize(values, Options);

    public static List<TEnum> Deserialize<TEnum>(string? json)
        where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<TEnum>>(json, Options) ?? [];

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }
}
