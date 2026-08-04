using System.Text.Json;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Claude.Anthropic;

/// <summary>
/// Parses the Anthropic Message Batches responses back into the provider-agnostic port types (SAD §5).
/// Pure functions over saved payloads — no HTTP — so the whole mapping is asserted with zero network.
/// A per-item provider error is mapped to <see cref="BatchResultItem.ProviderError"/>, never thrown:
/// one bad item is one recorded failure, not a failed batch (QG-3). The tool-use payload is lifted out
/// verbatim into <see cref="BatchResultItem.RawJson"/>; the domain's tolerant parser (T08) is the one
/// place that interprets it.
/// </summary>
internal static class AnthropicResponseParser
{
    /// <summary>Reads the provider batch id from a submit or status response body.</summary>
    public static string ParseBatchId(string responseBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseBody);
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("id").GetString()
            ?? throw new FormatException("Anthropic batch response carried no id.");
    }

    /// <summary>
    /// Reads a batch-list response body into the <see cref="ProviderBatchRef"/> vocabulary reconciliation
    /// works in (SAD §11 D5). The <c>data</c> array carries each batch's <c>id</c> and <c>created_at</c>;
    /// an entry missing either is skipped rather than throwing, so a provider adding fields never breaks
    /// the read. Order is preserved as returned (the API lists most-recent-first).
    /// </summary>
    public static IReadOnlyList<ProviderBatchRef> ParseBatchList(string responseBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseBody);
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var refs = new List<ProviderBatchRef>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return refs;
        }

        foreach (var entry in data.EnumerateArray())
        {
            if (!entry.TryGetProperty("id", out var idElement) || idElement.GetString() is not { } id
                || !entry.TryGetProperty("created_at", out var createdElement)
                || !createdElement.TryGetDateTimeOffset(out var createdAt))
            {
                continue;
            }

            refs.Add(new ProviderBatchRef(id, createdAt));
        }

        return refs;
    }

    /// <summary>
    /// Maps the status body's <c>processing_status</c> and <c>request_counts</c> onto the small
    /// provider-agnostic <see cref="BatchStatus"/> vocabulary. Only <see cref="ProviderBatchState.Ended"/>
    /// triggers retrieval.
    /// </summary>
    public static BatchStatus ParseStatus(string responseBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseBody);
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var status = root.GetProperty("processing_status").GetString() ?? string.Empty;
        var state = status switch
        {
            "in_progress" => ProviderBatchState.InProgress,
            "ended" => ProviderBatchState.Ended,
            "canceling" or "cancelled" or "canceled" => ProviderBatchState.Cancelled,
            "expired" => ProviderBatchState.Expired,
            _ => ProviderBatchState.InProgress,
        };

        var succeeded = 0;
        var errored = 0;
        var processing = 0;
        if (root.TryGetProperty("request_counts", out var counts))
        {
            succeeded = ReadCount(counts, "succeeded");
            errored = ReadCount(counts, "errored") + ReadCount(counts, "canceled") + ReadCount(counts, "expired");
            processing = ReadCount(counts, "processing");
        }

        return new BatchStatus(state, succeeded, errored, processing);
    }

    /// <summary>
    /// Maps one JSONL result line into a <see cref="BatchResultItem"/>. A <c>succeeded</c> result whose
    /// message contains the tool-use block yields <see cref="BatchResultItem.RawJson"/> (the tool input,
    /// verbatim); every other outcome — an <c>errored</c> result, a <c>canceled</c>/<c>expired</c> item,
    /// or a message with no tool call — yields <see cref="BatchResultItem.ProviderError"/>. This method
    /// never throws for a per-item shape; only a line that is not JSON is a fault.
    /// </summary>
    public static BatchResultItem ParseResultLine(string jsonlLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonlLine);
        using var doc = JsonDocument.Parse(jsonlLine);
        var root = doc.RootElement;

        var customId = root.TryGetProperty("custom_id", out var id) ? id.GetString() ?? string.Empty : string.Empty;

        if (!root.TryGetProperty("result", out var result))
        {
            return new BatchResultItem(customId, null, "Result envelope carried no result object.", TokenUsage.Zero);
        }

        var resultType = result.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (resultType != "succeeded")
        {
            return new BatchResultItem(customId, null, DescribeNonSuccess(result, resultType), TokenUsage.Zero);
        }

        if (!result.TryGetProperty("message", out var message))
        {
            return new BatchResultItem(customId, null, "Succeeded result carried no message.", TokenUsage.Zero);
        }

        var usage = ReadUsage(message);

        if (!TryExtractToolInput(message, out var rawJson))
        {
            return new BatchResultItem(customId, null, "Message carried no tool_use block.", usage);
        }

        return new BatchResultItem(customId, rawJson, null, usage);
    }

    private static bool TryExtractToolInput(JsonElement message, out string? rawJson)
    {
        rawJson = null;
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var bt) && bt.GetString() == "tool_use"
                && block.TryGetProperty("input", out var input))
            {
                rawJson = input.GetRawText();
                return true;
            }
        }

        return false;
    }

    private static string DescribeNonSuccess(JsonElement result, string? resultType)
    {
        // The provider's error type is safe to surface; we do not echo the whole body (it may be large).
        if (result.TryGetProperty("error", out var error))
        {
            var errType = error.TryGetProperty("type", out var et) ? et.GetString() : null;
            if (!string.IsNullOrWhiteSpace(errType))
            {
                return $"Provider result '{resultType}': {errType}.";
            }
        }

        return $"Provider result '{resultType ?? "unknown"}'.";
    }

    private static TokenUsage ReadUsage(JsonElement message)
    {
        if (!message.TryGetProperty("usage", out var usage))
        {
            return TokenUsage.Zero;
        }

        var input = usage.TryGetProperty("input_tokens", out var it) && it.ValueKind == JsonValueKind.Number
            ? it.GetInt32()
            : 0;
        var output = usage.TryGetProperty("output_tokens", out var ot) && ot.ValueKind == JsonValueKind.Number
            ? ot.GetInt32()
            : 0;
        // The prompt-cache hit: the input tokens served from the cached CV prefix at the reduced rate. Its
        // presence on every item after the first is what the CV-cache assertion checks (F4 T13). Absent on a
        // provider or model without caching, so it defaults to zero rather than throwing.
        var cacheRead = usage.TryGetProperty("cache_read_input_tokens", out var cr) && cr.ValueKind == JsonValueKind.Number
            ? cr.GetInt32()
            : 0;
        return new TokenUsage(input, output, cacheRead);
    }

    private static int ReadCount(JsonElement counts, string name) =>
        counts.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}
