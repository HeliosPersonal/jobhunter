using System.Globalization;
using System.Text.Json;
using JobHunter.Domain.Common;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization.Providers;

/// <summary>
/// Extracts the canonical fields from a Workable posting payload (contract §Workable). Workable gives
/// <c>title</c>, HTML <c>description</c>, an <c>application_url</c>, structured <c>country</c>/<c>city</c>
/// parts, a <c>telecommuting</c> boolean, and <c>published_on</c> — a date with no time, stored at midnight
/// UTC and flagged day-granular so "posted today" stays honest. <c>telecommuting</c> is authoritative when
/// true; otherwise the policy falls to Onsite given a named place.
/// </summary>
public sealed class WorkablePostingNormalizer : JsonPostingNormalizer
{
    public override Domain.Companies.AtsKind Kind => Domain.Companies.AtsKind.Workable;

    protected override Result<ExtractedPosting> ExtractFrom(JsonElement root)
    {
        var country = ReadString(root, "country");
        var city = ReadString(root, "city");
        var locations = LocationParser.FromParts(country, city: city);

        var (postedAt, granularity) = ReadPublishedOn(root);

        return new ExtractedPosting
        {
            Title = ReadString(root, "title"),
            ApplyUrl = ReadString(root, "application_url"),
            Description = PlainText.FromHtml(ReadString(root, "description")),
            Locations = locations,
            RemoteSignal = ReadBool(root, "telecommuting") == true ? RemotePolicy.Remote : null,
            PostedAt = postedAt,
            PostedAtGranularity = granularity,
        };
    }

    private static (DateTimeOffset? PostedAt, PostedAtGranularity Granularity) ReadPublishedOn(JsonElement root)
    {
        var text = ReadString(root, "published_on");
        if (text is not null
            && DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return (new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), PostedAtGranularity.Day);
        }

        return (null, PostedAtGranularity.Day);
    }
}
