using System.Text.Json;
using JobHunter.Domain.Companies;
using JobHunter.Scrapers.Http;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Adapters;

/// <summary>
/// The Workable adapter (contract §Workable). Postings live under <c>jobs</c>; the external id is the
/// <c>shortcode</c>. No volatile fields were observed. Workable's <c>published_on</c> is a date with no
/// time — F2 stores it at midnight UTC and flags it day-granular for freshness ranking; F1 preserves the
/// field verbatim rather than interpreting it.
/// </summary>
public sealed class WorkableJobSource(GatedHttpClient http, ILogger<WorkableJobSource> logger)
    : JsonBoardJobSource(http, logger)
{
    private const string BaseUrl = "https://apply.workable.com/api/v1/widget/accounts";

    public override AtsKind Kind => AtsKind.Workable;

    protected override string? ArrayProperty => "jobs";

    protected override IReadOnlySet<string> VolatileKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal);

    protected override string BuildUrl(AtsBinding binding) =>
        $"{BaseUrl}/{Uri.EscapeDataString(binding.BoardToken)}?details=true";

    protected override string? ReadExternalId(JsonElement posting) =>
        posting.TryGetProperty("shortcode", out var code) && code.ValueKind is JsonValueKind.String
            ? code.GetString()
            : null;
}
