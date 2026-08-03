using System.Text.Json;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Claude.Ollama;

/// <summary>
/// Parses an Ollama <c>/api/chat</c> response back into the provider-agnostic port types (SAD §3). Pure
/// functions over saved payloads — no HTTP — so the mapping is asserted with zero network. A response the
/// model could not satisfy (no message content) is mapped to <see cref="BatchResultItem.ProviderError"/>,
/// never thrown: one bad item is one recorded failure, matching the Anthropic adapter's contract exactly
/// (QG-3). The structured content is lifted out verbatim into <see cref="BatchResultItem.RawJson"/>; the
/// domain's tolerant parser (T08) is the one place that interprets it.
/// </summary>
internal static class OllamaResponseParser
{
    /// <summary>
    /// Maps one chat response body for the item identified by <paramref name="customId"/>. Ollama returns
    /// the schema-constrained object as the assistant message's <c>content</c> string; that string is the
    /// raw JSON the parser reads. Token usage comes from <c>prompt_eval_count</c> and <c>eval_count</c>.
    /// </summary>
    public static BatchResultItem ParseChatResponse(string customId, string responseBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customId);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseBody);

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var usage = ReadUsage(root);

        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            return new BatchResultItem(customId, null, "Ollama response carried no message content.", usage);
        }

        var raw = content.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new BatchResultItem(customId, null, "Ollama response content was empty.", usage);
        }

        return new BatchResultItem(customId, raw, null, usage);
    }

    private static TokenUsage ReadUsage(JsonElement root)
    {
        var input = root.TryGetProperty("prompt_eval_count", out var pe) && pe.ValueKind == JsonValueKind.Number
            ? pe.GetInt32()
            : 0;
        var output = root.TryGetProperty("eval_count", out var ec) && ec.ValueKind == JsonValueKind.Number
            ? ec.GetInt32()
            : 0;
        return new TokenUsage(input, output);
    }
}
