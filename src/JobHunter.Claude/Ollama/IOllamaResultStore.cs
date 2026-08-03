using System.Collections.Concurrent;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Claude.Ollama;

/// <summary>
/// Holds the results of a synthesised Ollama "batch" between the synchronous submit that produces them and
/// the poll/retrieve calls that read them. Ollama has no server-side batch API, so the fallback adapter
/// runs each item synchronously at submit time and keeps the outcomes here, then serves the same
/// <see cref="ILlmBatchClient"/> lifecycle over them — that is what lets Ollama be a second adapter rather
/// than a fork in the pipeline (SAD S6). The store is process-local and best-effort by design: Ollama is
/// the fallback whose absence degrades quality, not availability, so a lost in-memory batch is a re-run,
/// never a failed Run.
/// </summary>
internal interface IOllamaResultStore
{
    void Save(string batchId, DateTimeOffset createdAt, IReadOnlyList<BatchResultItem> items);

    bool TryGet(string batchId, out IReadOnlyList<BatchResultItem> items);

    IReadOnlyList<ProviderBatchRef> ListSince(DateTimeOffset createdOnOrAfter);
}

/// <summary>The default in-memory implementation, registered as a singleton so it spans adapter instances.</summary>
internal sealed class InMemoryOllamaResultStore : IOllamaResultStore
{
    private readonly ConcurrentDictionary<string, Entry> _batches = new(StringComparer.Ordinal);

    public void Save(string batchId, DateTimeOffset createdAt, IReadOnlyList<BatchResultItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentNullException.ThrowIfNull(items);
        _batches[batchId] = new Entry(createdAt, items);
    }

    public bool TryGet(string batchId, out IReadOnlyList<BatchResultItem> items)
    {
        if (batchId is not null && _batches.TryGetValue(batchId, out var entry))
        {
            items = entry.Items;
            return true;
        }

        items = [];
        return false;
    }

    public IReadOnlyList<ProviderBatchRef> ListSince(DateTimeOffset createdOnOrAfter) =>
        _batches
            .Where(kvp => kvp.Value.CreatedAt >= createdOnOrAfter)
            .OrderByDescending(kvp => kvp.Value.CreatedAt)
            .Select(kvp => new ProviderBatchRef(kvp.Key, kvp.Value.CreatedAt))
            .ToList();

    private sealed record Entry(DateTimeOffset CreatedAt, IReadOnlyList<BatchResultItem> Items);
}
