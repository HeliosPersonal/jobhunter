using System.Globalization;
using System.Text.Json;
using JobHunter.Domain.Common;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization.Providers;

/// <summary>
/// Extracts the canonical fields from an Ashby posting payload (contract §Ashby). Ashby gives <c>title</c>,
/// <c>descriptionPlain</c>, an <c>applyUrl</c>, a free-text <c>location</c> with optional
/// <c>secondaryLocations</c>, an explicit <c>isRemote</c> boolean, an <c>employmentType</c>, and — uniquely
/// — structured compensation under <c>compensation.compensationTierSummary</c>, which F2 parses into a
/// salary range and otherwise retains raw. <c>publishedAt</c> is an exact instant. The <c>isRemote</c> flag
/// is authoritative when true; when false the policy is left to inference from the (present) location text.
/// </summary>
public sealed class AshbyPostingNormalizer : JsonPostingNormalizer
{
    public override Domain.Companies.AtsKind Kind => Domain.Companies.AtsKind.Ashby;

    protected override Result<ExtractedPosting> ExtractFrom(JsonElement root)
    {
        var compensation = ReadObject(root, "compensation");
        var salaryText = compensation is null
            ? null
            : ReadString(compensation.Value, "compensationTierSummary");

        var (postedAt, granularity) = ReadPublishedAt(root);

        return new ExtractedPosting
        {
            Title = ReadString(root, "title"),
            ApplyUrl = ReadString(root, "applyUrl"),
            Description = ReadString(root, "descriptionPlain") ?? string.Empty,
            LocationText = BuildLocationText(root),
            RemoteSignal = ReadBool(root, "isRemote") == true ? RemotePolicy.Remote : null,
            EmploymentType = EmploymentTypeParser.Parse(ReadString(root, "employmentType")),
            SalaryText = salaryText,
            PostedAt = postedAt,
            PostedAtGranularity = granularity,
        };
    }

    private static string? BuildLocationText(JsonElement root)
    {
        var parts = new List<string>();

        var primary = ReadString(root, "location");
        if (primary is not null)
        {
            parts.Add(primary);
        }

        if (root.TryGetProperty("secondaryLocations", out var secondary)
            && secondary.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in secondary.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String
                    && entry.GetString() is { Length: > 0 } text
                    && !string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text.Trim());
                }
            }
        }

        return parts.Count == 0 ? null : string.Join(';', parts);
    }

    private static (DateTimeOffset? PostedAt, PostedAtGranularity Granularity) ReadPublishedAt(JsonElement root)
    {
        var text = ReadString(root, "publishedAt");
        if (text is not null
            && DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var instant))
        {
            return (instant, PostedAtGranularity.Exact);
        }

        return (null, PostedAtGranularity.Exact);
    }
}
