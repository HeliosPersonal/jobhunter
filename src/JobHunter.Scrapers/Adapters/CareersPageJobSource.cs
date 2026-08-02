using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Postings;
using JobHunter.Scrapers.Http;
using JobHunter.Scrapers.Parsing;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Adapters;

/// <summary>
/// The reference Tier-2 adapter (contract §Career pages, T07). It fetches a company careers page and reads
/// <c>schema.org/JobPosting</c> nodes from its <c>&lt;script type="application/ld+json"&gt;</c> blocks,
/// tolerating multiple blocks, <c>@graph</c>/array wrapping and a single malformed block. It is
/// deliberately acquisition-only, like the JSON boards: it captures each JobPosting node verbatim and hashes
/// it with <c>dateModified</c> stripped, and leaves interpretation of <c>jobLocationType</c>,
/// <c>baseSalary</c> etc. to F2. The Tier-2 mark that lets ranking down-weight these is the binding's
/// <see cref="AtsKind.CareersPage"/> — pages vary too much for anything higher (confidence capped at 0.70).
/// When a node has no <c>identifier</c>, the apply <c>url</c> is hashed to synthesise a stable external id.
/// </summary>
public sealed class CareersPageJobSource(GatedHttpClient http, ILogger<CareersPageJobSource> logger)
    : IJobSource
{
    private const string SyntheticIdPrefix = "url:";

    private static readonly IReadOnlySet<string> VolatileKeys =
        new HashSet<string>(StringComparer.Ordinal) { "dateModified" };

    private static readonly IReadOnlyDictionary<string, Func<JsonElement, string>> NoTransforms =
        new Dictionary<string, Func<JsonElement, string>>();

    private readonly GatedHttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly ILogger<CareersPageJobSource> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public AtsKind Kind => AtsKind.CareersPage;

    /// <inheritdoc />
    public async IAsyncEnumerable<FetchedPosting> FetchAsync(
        AtsBinding binding,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);

        // The Tier-2 binding stores the full careers URL as its token — there is no provider API to build.
        var response = await _http.GetAsync(binding.BoardToken, cancellationToken).ConfigureAwait(false);
        if (response.Outcome != GatedOutcome.Ok || response.Body is null)
        {
            _logger.LogInformation(
                "Careers page {Url} did not return a body: {Outcome}", binding.BoardToken, response.Outcome);
            yield break;
        }

        var nodes = JsonLdExtractor.JobPostings(response.Body);

        var skipped = 0;
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var posting = TryReadPosting(node, ref skipped);
            if (posting is not null)
            {
                yield return posting;
            }
        }

        if (skipped > 0)
        {
            _logger.LogWarning(
                "Careers page {Url}: skipped {Skipped} JobPosting node(s) with no usable id of {Total}",
                binding.BoardToken, skipped, nodes.Count);
        }
    }

    private static FetchedPosting? TryReadPosting(JsonElement node, ref int skipped)
    {
        var externalId = ReadExternalId(node);
        if (externalId is null)
        {
            // No identifier and no apply URL to hash: nothing stable to key on, so skip and count.
            skipped++;
            return null;
        }

        var rawPayload = node.GetRawText();
        var canonical = CanonicalJson.Canonicalise(node, VolatileKeys, NoTransforms);
        var hash = ContentHash.Compute(canonical);
        return new FetchedPosting(externalId, rawPayload, hash.Value);
    }

    private static string? ReadExternalId(JsonElement node)
    {
        if (node.TryGetProperty("identifier", out var identifier))
        {
            var value = ReadIdentifier(identifier);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        // A missing identifier synthesises one by hashing the apply URL (T07 "Done when").
        if (node.TryGetProperty("url", out var url)
            && url.ValueKind == JsonValueKind.String
            && url.GetString() is { Length: > 0 } applyUrl)
        {
            return SyntheticIdPrefix + ContentHash.Compute(applyUrl).Value;
        }

        return null;
    }

    private static string? ReadIdentifier(JsonElement identifier)
    {
        // schema.org identifier is either a plain string/number or a PropertyValue with a "value".
        switch (identifier.ValueKind)
        {
            case JsonValueKind.String:
                return identifier.GetString();

            case JsonValueKind.Number:
                return identifier.GetRawText();

            case JsonValueKind.Object when identifier.TryGetProperty("value", out var value):
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null,
                };

            default:
                return null;
        }
    }
}
