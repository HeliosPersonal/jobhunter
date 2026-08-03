using System.Text.Json;
using JobHunter.Domain.Jobs;

namespace JobHunter.Infrastructure.Persistence.Jobs;

/// <summary>
/// The one serialisation of a <see cref="LocationSet"/> to and from the <c>jobs.locations</c> jsonb
/// column (<c>[{country, region, city}]</c>). Shared by the EF configuration and the conflict-tolerant
/// insert repository so both write byte-identical JSON — the fingerprint never depends on serialisation,
/// but a single shape keeps the read side and the write side honest. An empty set is a legal empty array.
/// </summary>
internal static class LocationSetJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(LocationSet set) =>
        JsonSerializer.Serialize(
            set.Locations.Select(l => new LocationRow(l.Country, l.Region, l.City)),
            Options);

    public static LocationSet Deserialize(string json)
    {
        var rows = JsonSerializer.Deserialize<List<LocationRow>>(json, Options) ?? [];
        var locations = rows
            .Select(r => JobLocation.TryCreate(r.Country, r.Region, r.City))
            .Where(result => result.IsSuccess)
            .Select(result => result.Value);
        return LocationSet.Of(locations);
    }

    private sealed record LocationRow(string Country, string? Region, string? City);
}
