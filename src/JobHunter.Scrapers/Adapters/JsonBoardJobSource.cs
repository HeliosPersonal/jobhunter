using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Postings;
using JobHunter.Scrapers.Http;
using JobHunter.Scrapers.Parsing;
using Microsoft.Extensions.Logging;

namespace JobHunter.Scrapers.Adapters;

/// <summary>
/// The shared body of the four JSON-board adapters (Greenhouse, Lever, Ashby, Workable). It owns the one
/// hard part — streaming a board so a 400-posting response is processed with bounded memory (SAD §5),
/// and surviving a malformed posting inside an otherwise valid board (QG-1) — and leaves each provider to
/// declare only what differs: the URL, where the array lives, how the external id is read, which fields
/// are volatile, and which fields are transformed before hashing.
/// </summary>
public abstract class JsonBoardJobSource(GatedHttpClient http, ILogger logger) : IJobSource
{
    private readonly GatedHttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public abstract AtsKind Kind { get; }

    /// <summary>The absolute board URL for <paramref name="binding"/>'s token.</summary>
    protected abstract string BuildUrl(AtsBinding binding);

    /// <summary>The property holding the posting array, or <see langword="null"/> when the root is the array.</summary>
    protected abstract string? ArrayProperty { get; }

    /// <summary>Top-level keys stripped before hashing so a cosmetic re-fetch is not a change (AC-02).</summary>
    protected abstract IReadOnlySet<string> VolatileKeys { get; }

    /// <summary>Top-level keys whose value is replaced (as text) before hashing — e.g. HTML → plain text.</summary>
    protected virtual IReadOnlyDictionary<string, Func<JsonElement, string>> HashTransforms { get; } =
        EmptyTransforms;

    /// <summary>Reads the provider's own posting id, or <see langword="null"/> if it is absent (skip + count).</summary>
    protected abstract string? ReadExternalId(JsonElement posting);

    /// <inheritdoc />
    public async IAsyncEnumerable<FetchedPosting> FetchAsync(
        AtsBinding binding,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var url = BuildUrl(binding);
        var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.Outcome != GatedOutcome.Ok || response.Body is null)
        {
            // Not our failure domain here: the caller reads the outcome to log/requeue. Streaming just ends.
            _logger.LogInformation(
                "{Kind} fetch of board {BoardToken} did not return a body: {Outcome}",
                Kind, binding.BoardToken, response.Outcome);
            yield break;
        }

        var utf8 = Encoding.UTF8.GetBytes(response.Body);
        var ranges = JsonArrayStreamer.ElementRanges(utf8, ArrayProperty);

        var skipped = 0;
        foreach (var range in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var posting = TryReadPosting(utf8, range, binding, ref skipped);
            if (posting is not null)
            {
                yield return posting;
            }
        }

        if (skipped > 0)
        {
            _logger.LogWarning(
                "{Kind} board {BoardToken}: skipped {Skipped} malformed posting(s) of {Total}",
                Kind, binding.BoardToken, skipped, ranges.Count);
        }
    }

    private FetchedPosting? TryReadPosting(byte[] utf8, Range range, AtsBinding binding, ref int skipped)
    {
        var slice = utf8.AsSpan(range);
        try
        {
            using var document = JsonDocument.Parse(slice.ToArray());
            var root = document.RootElement;

            var externalId = ReadExternalId(root);
            if (string.IsNullOrWhiteSpace(externalId))
            {
                skipped++;
                return null;
            }

            var rawPayload = Encoding.UTF8.GetString(slice);
            var canonical = CanonicalJson.Canonicalise(root, VolatileKeys, HashTransforms);
            var hash = ContentHash.Compute(canonical);
            return new FetchedPosting(externalId, rawPayload, hash.Value);
        }
        catch (JsonException)
        {
            // One malformed posting inside a valid board is skipped and counted, never fatal (QG-1).
            skipped++;
            return null;
        }
    }

    private static readonly IReadOnlyDictionary<string, Func<JsonElement, string>> EmptyTransforms =
        new Dictionary<string, Func<JsonElement, string>>();
}
