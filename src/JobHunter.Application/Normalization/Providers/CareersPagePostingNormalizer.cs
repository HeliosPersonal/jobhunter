using System.Globalization;
using System.Text.Json;
using JobHunter.Domain.Common;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization.Providers;

/// <summary>
/// Extracts the canonical fields from a schema.org <c>JobPosting</c> JSON-LD node captured from a company
/// careers page (contract §Career pages). It reads <c>title</c>, HTML <c>description</c>, the apply
/// <c>url</c>, the <c>jobLocation.address</c> parts, <c>jobLocationType</c> (<c>TELECOMMUTE</c> ⇒ remote),
/// <c>employmentType</c>, a <c>baseSalary</c> monetary amount, and <c>datePosted</c> (date-granular). Every
/// job from here is marked Tier-2 (<see cref="ExtractedPosting.IsTier2"/>) — the lowest-confidence binding,
/// so ranking can down-weight it. Pages vary too much for anything higher.
/// </summary>
public sealed class CareersPagePostingNormalizer : JsonPostingNormalizer
{
    public override Domain.Companies.AtsKind Kind => Domain.Companies.AtsKind.CareersPage;

    protected override Result<ExtractedPosting> ExtractFrom(JsonElement root)
    {
        var (postedAt, granularity) = ReadDatePosted(root);

        return new ExtractedPosting
        {
            Title = ReadString(root, "title"),
            ApplyUrl = ReadString(root, "url"),
            Description = PlainText.FromHtml(ReadString(root, "description")),
            Locations = ReadLocation(root),
            RemoteSignal = IsTelecommute(root) ? RemotePolicy.Remote : null,
            EmploymentType = EmploymentTypeParser.Parse(ReadString(root, "employmentType")),
            SalaryText = ReadBaseSalary(root),
            PostedAt = postedAt,
            PostedAtGranularity = granularity,
            IsTier2 = true,
        };
    }

    private static bool IsTelecommute(JsonElement root)
    {
        var type = ReadString(root, "jobLocationType");
        return type is not null
            && string.Equals(type, "TELECOMMUTE", StringComparison.OrdinalIgnoreCase);
    }

    private static LocationSet? ReadLocation(JsonElement root)
    {
        var jobLocation = ReadObject(root, "jobLocation");
        var address = jobLocation is null ? null : ReadObject(jobLocation.Value, "address");
        if (address is null)
        {
            return null;
        }

        var country = ReadString(address.Value, "addressCountry");
        var region = ReadString(address.Value, "addressRegion");
        var city = ReadString(address.Value, "addressLocality");

        var set = LocationParser.FromParts(country, region, city);
        return set.IsEmpty ? null : set;
    }

    private static string? ReadBaseSalary(JsonElement root)
    {
        var baseSalary = ReadObject(root, "baseSalary");
        if (baseSalary is null)
        {
            return null;
        }

        var currency = ReadString(baseSalary.Value, "currency");
        var value = ReadObject(baseSalary.Value, "value");
        if (currency is null || value is null)
        {
            return null;
        }

        var min = ReadNumber(value.Value, "minValue");
        var max = ReadNumber(value.Value, "maxValue");
        if (min is null && max is null)
        {
            return null;
        }

        // Re-serialise to the free-text form SalaryParser understands ("EUR 90000 - 120000 per year").
        var unit = ReadString(value.Value, "unitText");
        var period = MapUnit(unit);
        var amounts = min is not null && max is not null
            ? FormattableString.Invariant($"{min} - {max}")
            : FormattableString.Invariant($"{min ?? max}");
        return FormattableString.Invariant($"{currency} {amounts} per {period}");
    }

    private static string MapUnit(string? unitText) =>
        unitText?.ToUpperInvariant() switch
        {
            "HOUR" => "hour",
            "DAY" => "day",
            "MONTH" => "month",
            _ => "year",
        };

    private static decimal? ReadNumber(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return null;
    }

    private static (DateTimeOffset? PostedAt, PostedAtGranularity Granularity) ReadDatePosted(JsonElement root)
    {
        var text = ReadString(root, "datePosted");
        if (text is null)
        {
            return (null, PostedAtGranularity.Day);
        }

        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return (new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), PostedAtGranularity.Day);
        }

        if (DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var instant))
        {
            return (instant, PostedAtGranularity.Exact);
        }

        return (null, PostedAtGranularity.Day);
    }
}
