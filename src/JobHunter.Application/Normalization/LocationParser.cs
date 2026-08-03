using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Turns a provider's location text — structured parts or a free-text blob — into a
/// <see cref="LocationSet"/> (T03, SAD §8). The parser is deliberately forgiving on input and
/// conservative on output: it splits on the separators providers actually use, strips remote/anywhere
/// noise words that are policy signals rather than places, and yields an empty set (a legal, fully-remote
/// state) rather than inventing a location it cannot defend. No clock, no randomness, and only ordinal
/// comparison and invariant casing, so the same text parses identically everywhere.
/// </summary>
public static class LocationParser
{
    private static readonly char[] Separators = [';', '/', '\n', '\r', '\t'];

    // Tokens that describe a working arrangement, not a place. They are removed before a fragment is
    // treated as a location so "US (Remote)" contributes the country and not a phantom "remote" city.
    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "remote", "anywhere", "hybrid", "onsite", "on-site", "in-office", "worldwide", "global",
        "distributed", "flexible", "wfh", "work from home",
    };

    /// <summary>
    /// Parses free-text location <paramref name="text"/> into a set. Returns <see cref="LocationSet.Empty"/>
    /// when nothing survives noise-word removal (a fully-remote posting).
    /// </summary>
    public static LocationSet Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LocationSet.Empty;
        }

        var locations = new List<JobLocation>();
        foreach (var fragment in SplitFragments(text))
        {
            var location = ParseFragment(fragment);
            if (location is not null)
            {
                locations.Add(location);
            }
        }

        return LocationSet.Of(locations);
    }

    /// <summary>
    /// Builds a set from already-structured parts (one location), plus any additional free-text
    /// <paramref name="secondaryText"/> a provider supplies. Any part may be null; an all-null primary
    /// contributes nothing.
    /// </summary>
    public static LocationSet FromParts(
        string? country,
        string? region = null,
        string? city = null,
        string? secondaryText = null)
    {
        var locations = new List<JobLocation>();

        var primary = JobLocation.TryCreate(country, region, city);
        if (primary.IsSuccess)
        {
            locations.Add(primary.Value);
        }
        else if (city is not null || region is not null)
        {
            // A city or region with no country still names a place; keep it under an unknown country so a
            // Berlin-only posting is not silently dropped.
            var fallback = JobLocation.TryCreate(city ?? region, region: city is null ? null : region);
            if (fallback.IsSuccess)
            {
                locations.Add(fallback.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            locations.AddRange(Parse(secondaryText).Locations);
        }

        return LocationSet.Of(locations);
    }

    private static string[] SplitFragments(string text)
    {
        return text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static JobLocation? ParseFragment(string fragment)
    {
        // Strip bracketed decoration ("US (Remote)" → "US"), then split the comma-delimited parts.
        var cleaned = StripBrackets(fragment).Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        var parts = cleaned
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !NoiseWords.Contains(p))
            .ToArray();

        if (parts.Length == 0)
        {
            return null;
        }

        // Providers write "City, Region, Country" most to least specific. The last part is the country;
        // earlier parts are city then region. A single part is treated as the country.
        return parts.Length switch
        {
            1 => Build(parts[0]),
            2 => Build(parts[1], city: parts[0]),
            _ => Build(parts[^1], region: parts[^2], city: parts[0]),
        };
    }

    private static JobLocation? Build(string country, string? region = null, string? city = null)
    {
        var result = JobLocation.TryCreate(country, region, city);
        return result.IsSuccess ? result.Value : null;
    }

    private static string StripBrackets(string value)
    {
        var open = value.IndexOfAny(['(', '[', '{']);
        if (open < 0)
        {
            return value;
        }

        var close = value.IndexOfAny([')', ']', '}'], open);
        if (close < 0)
        {
            return value[..open];
        }

        return (value[..open] + value[(close + 1)..]).Trim();
    }
}
