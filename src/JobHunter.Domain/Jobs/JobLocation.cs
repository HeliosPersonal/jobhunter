using JobHunter.Domain.Common;

namespace JobHunter.Domain.Jobs;

/// <summary>
/// A single place a job may be worked: country, and optionally region and city (data-model §jobs
/// <c>locations</c> — <c>[{country, region, city}]</c>). Each part is trimmed and case-folded for its
/// comparison <see cref="Key"/>, but the displayed values are preserved as given. A location with no
/// country at all is not a location — <see cref="TryCreate"/> refuses it — because the country is the
/// only part a fingerprint can safely rely on.
/// </summary>
public sealed class JobLocation : ValueObject
{
    public static readonly Error Empty =
        new("job.location.empty", "A location requires at least a non-blank country.");

    private JobLocation(string country, string? region, string? city)
    {
        Country = country;
        Region = region;
        City = city;
    }

    /// <summary>The country, as published (never blank).</summary>
    public string Country { get; }

    /// <summary>The region/state, as published, or null.</summary>
    public string? Region { get; }

    /// <summary>The city, as published, or null.</summary>
    public string? City { get; }

    /// <summary>
    /// The order-insensitive, culture-invariant comparison key: the three parts lower-cased and joined
    /// by <c>|</c>, with missing parts left empty. This is what a <see cref="LocationSet"/> sorts and a
    /// fingerprint consumes, so it must never depend on the ambient culture.
    /// </summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>
    /// Creates a location from raw parts, trimming each and dropping blanks to null. Returns a failure
    /// (never throws) when the country is blank.
    /// </summary>
    public static Result<JobLocation> TryCreate(string? country, string? region = null, string? city = null)
    {
        var trimmedCountry = Clean(country);
        if (trimmedCountry is null)
        {
            return Empty;
        }

        var location = new JobLocation(trimmedCountry, Clean(region), Clean(city));
        location.Key = BuildKey(location);
        return Result<JobLocation>.Success(location);
    }

    public override string ToString() =>
        string.Join(", ", new[] { City, Region, Country }.Where(p => !string.IsNullOrEmpty(p)));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Key;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string BuildKey(JobLocation location)
    {
        var country = location.Country.ToLowerInvariant();
        var region = location.Region?.ToLowerInvariant() ?? string.Empty;
        var city = location.City?.ToLowerInvariant() ?? string.Empty;
        return string.Join('|', country, region, city);
    }
}
