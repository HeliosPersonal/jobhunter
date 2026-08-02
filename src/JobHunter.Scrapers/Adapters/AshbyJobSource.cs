using System.Text.Json;
using JobHunter.Domain.Companies;
using JobHunter.Scrapers.Http;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Adapters;

/// <summary>
/// The Ashby adapter (contract §Ashby). Postings live under <c>jobs</c>; <c>updatedAt</c> is volatile and
/// stripped before hashing. Ashby is the only provider that routinely publishes structured compensation
/// (<c>compensation.compensationTierSummary</c>): that field is preserved verbatim in the payload so F2
/// can parse it into structured salary and retain an unparseable tier string raw — F1 acquires it intact
/// and never coerces it.
/// </summary>
public sealed class AshbyJobSource(GatedHttpClient http, ILogger<AshbyJobSource> logger)
    : JsonBoardJobSource(http, logger)
{
    private const string BaseUrl = "https://api.ashbyhq.com/posting-api/job-board";

    public override AtsKind Kind => AtsKind.Ashby;

    protected override string? ArrayProperty => "jobs";

    protected override IReadOnlySet<string> VolatileKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "updatedAt" };

    protected override string BuildUrl(AtsBinding binding) =>
        $"{BaseUrl}/{Uri.EscapeDataString(binding.BoardToken)}?includeCompensation=true";

    protected override string? ReadExternalId(JsonElement posting) =>
        posting.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String
            ? id.GetString()
            : null;
}
