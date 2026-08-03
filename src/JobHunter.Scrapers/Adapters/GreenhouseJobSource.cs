using System.Text.Json;
using JobHunter.Domain.Companies;
using JobHunter.Scrapers.Http;
using JobHunter.Scrapers.Parsing;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Adapters;

/// <summary>
/// The Greenhouse adapter (contract §Greenhouse). Greenhouse is first because its <c>content</c> field is
/// HTML-escaped HTML — the most awkward of the five — so the decode-then-strip convention the others reuse
/// is forced here. The whole board is one response (no pagination); <c>updated_at</c> and
/// <c>requisition_id</c> are volatile, and <c>content</c> is hashed as its plain text so a markup-only
/// edit is not a change.
/// </summary>
public sealed class GreenhouseJobSource(GatedHttpClient http, ILogger<GreenhouseJobSource> logger)
    : JsonBoardJobSource(http, logger)
{
    private const string BaseUrl = "https://boards-api.greenhouse.io/v1/boards";

    public override AtsKind Kind => AtsKind.Greenhouse;

    protected override string? ArrayProperty => "jobs";

    protected override IReadOnlySet<string> VolatileKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "updated_at", "requisition_id" };

    protected override IReadOnlyDictionary<string, Func<JsonElement, string>> HashTransforms { get; } =
        new Dictionary<string, Func<JsonElement, string>>(StringComparer.Ordinal)
        {
            ["content"] = static value => HtmlText.ToPlainText(value.GetString()),
        };

    protected override string BuildUrl(AtsBinding binding) =>
        $"{BaseUrl}/{Uri.EscapeDataString(binding.BoardToken)}/jobs?content=true";

    protected override string? ReadExternalId(JsonElement posting) =>
        posting.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.Number
            ? id.GetRawText()
            : null;
}
