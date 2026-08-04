using System.Text.Json;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// Turns the market-note batch's single result item's raw tool-use JSON into the narrative text
/// (F5 T05). It is the Claude-side implementation of <see cref="INarrativeResultParser"/> and applies the
/// same never-throw discipline as the enrichment parser: a null/blank payload, malformed JSON, a non-object
/// root, or a missing/non-string/blank <c>narrative</c> is a recorded failure — never an exception — that
/// the synthesiser turns into a template fallback so the digest still ships (ADR-F5-0001).
/// </summary>
public sealed class NarrativeResultParser : INarrativeResultParser
{
    public NarrativeParseOutcome Parse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return NarrativeParseOutcome.Failure("empty result payload");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            return NarrativeParseOutcome.Failure($"malformed JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return NarrativeParseOutcome.Failure("result payload is not a JSON object");
            }

            if (!root.TryGetProperty(DigestNarrativeSchema.NarrativeField, out var el)
                || el.ValueKind != JsonValueKind.String)
            {
                return NarrativeParseOutcome.Failure("missing or non-string 'narrative'");
            }

            var narrative = el.GetString();
            if (string.IsNullOrWhiteSpace(narrative))
            {
                return NarrativeParseOutcome.Failure("blank 'narrative'");
            }

            return NarrativeParseOutcome.Success(narrative);
        }
    }
}
