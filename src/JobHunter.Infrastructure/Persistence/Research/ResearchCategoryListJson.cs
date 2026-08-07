using System.Text.Json;
using JobHunter.Domain.Research;

namespace JobHunter.Infrastructure.Persistence.Research;

/// <summary>
/// The one serialisation of a <see cref="ResearchCategory"/> list to and from a <c>jsonb</c> array column —
/// used for <c>company_research.categories_covered</c> and <c>categories_unavailable</c> (F8 data-model
/// §company_research). Categories travel as their <c>text</c> names (<c>["Layoffs","Funding"]</c>), never as
/// ordinals, so a reordered enum cannot silently reinterpret stored rows (coding-standards §5). An empty or
/// null column round-trips to an empty list — a dossier that covered nothing is still a dossier (AC-07).
/// </summary>
internal static class ResearchCategoryListJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<ResearchCategory> categories) =>
        JsonSerializer.Serialize(categories.Select(c => c.ToString()).ToList(), Options);

    public static List<ResearchCategory> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var names = JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];
        var categories = new List<ResearchCategory>();
        foreach (var name in names)
        {
            if (Enum.TryParse<ResearchCategory>(name, out var category))
            {
                categories.Add(category);
            }
        }

        return categories;
    }
}
