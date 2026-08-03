using JobHunter.Domain.Common;

namespace JobHunter.Domain.Jobs;

/// <summary>
/// The set of places a job may be worked, deduplicated and order-insensitive (ADR-F2-0001). Two
/// postings that list the same locations in a different order are the same set, so equality and the
/// fingerprint must not depend on order — <see cref="SortedKey"/> gives a deterministic,
/// culture-invariant serialisation for exactly that purpose. An <em>empty</em> set is legal and
/// meaningful: a fully-remote job has no location (data-model §jobs — "empty array is legal").
/// </summary>
public sealed class LocationSet : ValueObject
{
    private readonly List<JobLocation> _locations;

    private LocationSet(List<JobLocation> locations) => _locations = locations;

    /// <summary>The locations, in deterministic key order (deduplicated).</summary>
    public IReadOnlyList<JobLocation> Locations => _locations;

    public bool IsEmpty => _locations.Count == 0;

    public int Count => _locations.Count;

    /// <summary>
    /// The order-insensitive fingerprint key: each location's <see cref="JobLocation.Key"/>, sorted by
    /// ordinal, deduplicated, joined by newline. Deterministic across machines and cultures because it
    /// uses ordinal sorting and the already-lower-cased location keys.
    /// </summary>
    public string SortedKey =>
        string.Join('\n', _locations.Select(l => l.Key));

    /// <summary>The empty set — a fully-remote job with no stated location.</summary>
    public static LocationSet Empty { get; } = new([]);

    /// <summary>
    /// Builds a set from a sequence of locations, deduplicating by key and ordering deterministically.
    /// Null entries are rejected as a programmer error; an empty sequence yields <see cref="Empty"/>.
    /// </summary>
    public static LocationSet Of(IEnumerable<JobLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);

        var deduped = new Dictionary<string, JobLocation>(StringComparer.Ordinal);
        foreach (var location in locations)
        {
            ArgumentNullException.ThrowIfNull(location);
            deduped.TryAdd(location.Key, location);
        }

        var ordered = deduped.Values
            .OrderBy(l => l.Key, StringComparer.Ordinal)
            .ToList();

        return new LocationSet(ordered);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return SortedKey;
    }
}
