using System.Text.Json;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Claude.Ollama;

/// <summary>
/// Turns one provider-agnostic <see cref="BatchRequestItem"/> into an Ollama <c>/api/chat</c> request
/// body (SAD §3, ADR-0005). Like <see cref="Anthropic.AnthropicRequestBuilder"/> it is a pure function of
/// its inputs — no HTTP, no clock, no state — so the fallback adapter's request shape is asserted against
/// saved payloads with zero network. Structured output is bound through Ollama's <c>format</c> field,
/// which takes the very same JSON Schema the Anthropic tool-use envelope carries, so both providers emit
/// the identical wire shape and the one <see cref="Enrichment.TolerantJsonParser"/> interprets both.
/// </summary>
internal static class OllamaRequestBuilder
{
    /// <summary>
    /// Serialises one item to a non-streaming chat request. <paramref name="model"/> is the local Ollama
    /// tag; the CV never enters this body — an enrichment prompt describes the job, not the fit (SAD §2).
    /// </summary>
    public static string BuildChatBody(BatchRequestItem item, string model, int maxOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputTokens);

        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);

            // A single turn: the system prompt as system, the rendered job as user. Ollama echoes no ids,
            // so the custom id is tracked adapter-side against the request order, never sent on the wire.
            writer.WriteStartArray("messages");
            if (!string.IsNullOrEmpty(item.SystemPrompt))
            {
                writer.WriteStartObject();
                writer.WriteString("role", "system");
                writer.WriteString("content", item.SystemPrompt);
                writer.WriteEndObject();
            }

            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", item.UserContent);
            writer.WriteEndObject();
            writer.WriteEndArray();

            // Structured output: Ollama constrains generation to this JSON Schema exactly as the Anthropic
            // tool binds it, so the fallback emits the same object shape the tolerant parser expects (ADR-0006).
            writer.WritePropertyName("format");
            using (var schemaDoc = JsonDocument.Parse(item.OutputSchema.SchemaJson))
            {
                schemaDoc.RootElement.WriteTo(writer);
            }

            // Synchronous single response — the adapter synthesises the batch lifecycle around it (S6).
            writer.WriteBoolean("stream", false);

            writer.WriteStartObject("options");
            writer.WriteNumber("num_predict", maxOutputTokens);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
