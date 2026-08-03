using System.Text.Json;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Claude.Anthropic;

/// <summary>
/// Turns a provider-agnostic <see cref="BatchSubmission"/> into the Anthropic Message Batches request
/// body (SAD §5, ADR-0005). It is a pure function of its inputs — no HTTP, no clock, no state — so the
/// adapter's request shape is asserted against saved payloads with zero network (the <c>wisewizard</c>
/// pattern). Structured output is bound via a single tool plus a forced <c>tool_choice</c> so the model
/// must call it (ADR-0006); the schema text comes straight from <see cref="JsonSchema.SchemaJson"/>.
/// </summary>
internal static class AnthropicRequestBuilder
{
    /// <summary>
    /// Serialises the whole batch to the <c>{"requests":[...]}</c> body. <paramref name="modelId"/> is the
    /// tier's configured model (from the pricing table, the single place model ids live); the CV never
    /// enters this body — an enrichment prompt describes the job, not the fit (SAD §2).
    /// </summary>
    public static string BuildSubmitBody(BatchSubmission submission, string modelId, int maxOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputTokens);

        var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("requests");

            foreach (var item in submission.Items)
            {
                WriteRequest(writer, item, modelId, maxOutputTokens);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteRequest(Utf8JsonWriter writer, BatchRequestItem item, string modelId, int maxOutputTokens)
    {
        writer.WriteStartObject();
        writer.WriteString("custom_id", item.CustomId);

        writer.WriteStartObject("params");
        writer.WriteString("model", modelId);
        writer.WriteNumber("max_tokens", maxOutputTokens);

        if (!string.IsNullOrEmpty(item.SystemPrompt))
        {
            writer.WriteString("system", item.SystemPrompt);
        }

        writer.WriteStartArray("messages");
        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WriteString("content", item.UserContent);
        writer.WriteEndObject();
        writer.WriteEndArray();

        // One tool, whose input_schema is the enrichment schema, and a forced tool_choice: the model must
        // emit the structured object rather than prose (ADR-0006).
        writer.WriteStartArray("tools");
        writer.WriteStartObject();
        writer.WriteString("name", item.OutputSchema.ToolName);
        writer.WritePropertyName("input_schema");
        using (var schemaDoc = JsonDocument.Parse(item.OutputSchema.SchemaJson))
        {
            schemaDoc.RootElement.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.WriteEndArray();

        writer.WriteStartObject("tool_choice");
        writer.WriteString("type", "tool");
        writer.WriteString("name", item.OutputSchema.ToolName);
        writer.WriteEndObject();

        writer.WriteEndObject(); // params
        writer.WriteEndObject(); // request
    }
}
