using System.Globalization;
using System.Text.Json;
using JobHunter.Application.Deduplication;
using JobHunter.Application.Normalization;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Deduplication;

/// <summary>
/// QG-2: the fingerprint is frozen. Fifty recorded (domain, title, locations) inputs carry their expected
/// SHA-256 digest, computed once and checked into <c>Data/fingerprints.json</c>. The real
/// <see cref="FingerprintCalculator"/> must reproduce each one <em>byte for byte</em>, and it must do so under
/// three cultures — <c>en-US</c>, the invariant culture, and the notorious <c>tr-TR</c> (whose dotless-i
/// casing breaks any accidental culture-sensitive <c>ToLower</c>). A drift here is either an unversioned
/// algorithm change (forbidden without a migration, SAD §11 D3) or a culture leak; both fail the build.
/// </summary>
public sealed class FrozenFingerprintTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly List<FrozenCase> Cases = LoadCases();

    public static TheoryData<string> Cultures() => new("en-US", "", "tr-TR");

    [Fact]
    public void The_frozen_set_has_fifty_recorded_fingerprints()
    {
        Cases.Count.ShouldBe(50);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Every_frozen_fingerprint_reproduces_byte_for_byte_under_the_culture(string cultureName)
    {
        var culture = cultureName.Length == 0
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(cultureName);

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var mismatches = new List<string>();
            foreach (var frozen in Cases)
            {
                var actual = Compute(frozen);
                if (!string.Equals(actual, frozen.Fingerprint, StringComparison.Ordinal))
                {
                    mismatches.Add($"{frozen.Domain} / '{frozen.Title}': expected {frozen.Fingerprint}, got {actual}");
                }
            }

            mismatches.ShouldBeEmpty(
                $"Fingerprints drifted under culture '{cultureName}': " + string.Join("; ", mismatches));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static string Compute(FrozenCase frozen)
    {
        var normalisedTitle = TitleNormalizer.Normalize(frozen.Title).Value;

        var built = new List<JobLocation>();
        foreach (var location in frozen.Locations)
        {
            var created = JobLocation.TryCreate(location.Country, location.Region, location.City);
            if (created.IsSuccess)
            {
                built.Add(created.Value);
            }
        }

        var locations = built.Count == 0 ? LocationSet.Empty : LocationSet.Of(built);
        return FingerprintCalculator.Compute(frozen.Domain, normalisedTitle, locations).Value;
    }

    private static List<FrozenCase> LoadCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "fingerprints.json");
        using var stream = File.OpenRead(path);
        var cases = JsonSerializer.Deserialize<List<FrozenCase>>(stream, JsonOptions);
        return cases ?? throw new InvalidOperationException("The frozen fingerprint set failed to load.");
    }

    private sealed record FrozenCase(
        string Domain,
        string Title,
        IReadOnlyList<FrozenLocation> Locations,
        string Fingerprint);

    private sealed record FrozenLocation(string? Country, string? Region, string? City);
}
