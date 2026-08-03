using System.Text.Json;
using JobHunter.Domain.Common;

namespace JobHunter.Application.Normalization.Providers;

/// <summary>
/// The shared body of the JSON-payload provider normalisers (SAD §5). It owns the one awkward part — parsing
/// a stored posting payload defensively and turning a malformed one into a <see cref="Result{T}"/> failure
/// rather than an exception, so a single bad posting is recorded and skipped without halting the batch
/// (AC-04) — and leaves each provider to map its own field names in <see cref="ExtractFrom"/>. Extraction is
/// a pure function of the payload: no clock, no randomness, no I/O (SAD S5).
/// </summary>
public abstract class JsonPostingNormalizer : IPostingNormalizer
{
    public static readonly Error MalformedPayload =
        new("job.normalize.malformed_payload", "The stored posting payload is not valid JSON.");

    /// <inheritdoc />
    public abstract Domain.Companies.AtsKind Kind { get; }

    /// <inheritdoc />
    public Result<ExtractedPosting> Extract(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            // A payload that will not parse is a bad stored row, a business outcome, not a crash (AC-04).
            return MalformedPayload;
        }

        using (document)
        {
            return ExtractFrom(document.RootElement);
        }
    }

    /// <summary>Maps this provider's field names on <paramref name="root"/> to the canonical shape.</summary>
    protected abstract Result<ExtractedPosting> ExtractFrom(JsonElement root);

    /// <summary>The trimmed string at <paramref name="name"/>, or null when absent, null or blank.</summary>
    protected static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>The boolean at <paramref name="name"/>, or null when absent or not a boolean.</summary>
    protected static bool? ReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>The nested object at <paramref name="name"/>, or null when absent or not an object.</summary>
    protected static JsonElement? ReadObject(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        return null;
    }
}
