using System.Text.Json;
using JobHunter.Domain.Common;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization.Providers;

/// <summary>
/// Extracts the canonical fields from a Lever posting payload (contract §Lever). Lever gives <c>text</c>
/// (title), <c>descriptionPlain</c>, a <c>hostedUrl</c> apply link, and a <c>categories</c> object holding
/// the free-text <c>location</c> and the <c>commitment</c> employment type. Lever carries the cleanest
/// remote signal of the five — <c>workplaceType</c> (<c>remote</c>/<c>hybrid</c>/<c>on-site</c>) — which is
/// authoritative and wins over any inference from the location text.
/// </summary>
public sealed class LeverPostingNormalizer : JsonPostingNormalizer
{
    public override Domain.Companies.AtsKind Kind => Domain.Companies.AtsKind.Lever;

    protected override Result<ExtractedPosting> ExtractFrom(JsonElement root)
    {
        var categories = ReadObject(root, "categories");
        var locationText = categories is null ? null : ReadString(categories.Value, "location");
        var commitment = categories is null ? null : ReadString(categories.Value, "commitment");

        return new ExtractedPosting
        {
            Title = ReadString(root, "text"),
            ApplyUrl = ReadString(root, "hostedUrl"),
            Description = ReadString(root, "descriptionPlain") ?? string.Empty,
            LocationText = locationText,
            RemoteSignal = MapWorkplaceType(ReadString(root, "workplaceType")),
            EmploymentType = EmploymentTypeParser.Parse(commitment),
        };
    }

    private static RemotePolicy? MapWorkplaceType(string? workplaceType)
    {
        if (workplaceType is null)
        {
            return null;
        }

        return workplaceType.ToLowerInvariant() switch
        {
            "remote" => RemotePolicy.Remote,
            "hybrid" => RemotePolicy.Hybrid,
            "on-site" or "onsite" => RemotePolicy.Onsite,
            _ => null,
        };
    }
}
