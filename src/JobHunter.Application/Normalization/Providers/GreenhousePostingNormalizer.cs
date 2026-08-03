using System.Text.Json;
using JobHunter.Domain.Common;

namespace JobHunter.Application.Normalization.Providers;

/// <summary>
/// Extracts the canonical fields from a Greenhouse posting payload (contract §Greenhouse). Greenhouse gives
/// <c>title</c>, an <c>absolute_url</c> apply link, a <c>location.name</c> free-text location, and
/// HTML-escaped HTML in <c>content</c> — decoded to plain text here. Greenhouse publishes no explicit remote
/// signal, so the policy is inferred from the location text (never the description). No structured salary or
/// employment type is published, so those stay unset/Unknown.
/// </summary>
public sealed class GreenhousePostingNormalizer : JsonPostingNormalizer
{
    public override Domain.Companies.AtsKind Kind => Domain.Companies.AtsKind.Greenhouse;

    protected override Result<ExtractedPosting> ExtractFrom(JsonElement root)
    {
        var location = ReadObject(root, "location");
        var locationText = location is null ? null : ReadString(location.Value, "name");

        return new ExtractedPosting
        {
            Title = ReadString(root, "title"),
            ApplyUrl = ReadString(root, "absolute_url"),
            Description = PlainText.FromHtml(ReadString(root, "content")),
            LocationText = locationText,
        };
    }
}
