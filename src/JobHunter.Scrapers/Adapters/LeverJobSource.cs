using System.Text.Json;
using JobHunter.Domain.Companies;
using JobHunter.Scrapers.Http;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Adapters;

/// <summary>
/// The Lever adapter (contract §Lever). Lever's payload is a bare JSON array (no wrapping object) of
/// stable postings — no volatile fields, so every field participates in the hash. The external id is a
/// UUID string. Lever carries the cleanest remote signal of the five (<c>workplaceType</c>); that field
/// is preserved verbatim in the payload for F2 to normalise — F1 acquires, it does not interpret.
/// </summary>
public sealed class LeverJobSource(GatedHttpClient http, ILogger<LeverJobSource> logger)
    : JsonBoardJobSource(http, logger)
{
    private const string BaseUrl = "https://api.lever.co/v0/postings";

    public override AtsKind Kind => AtsKind.Lever;

    protected override string? ArrayProperty => null;

    protected override IReadOnlySet<string> VolatileKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal);

    protected override string BuildUrl(AtsBinding binding) =>
        $"{BaseUrl}/{Uri.EscapeDataString(binding.BoardToken)}?mode=json";

    protected override string? ReadExternalId(JsonElement posting) =>
        posting.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String
            ? id.GetString()
            : null;
}
