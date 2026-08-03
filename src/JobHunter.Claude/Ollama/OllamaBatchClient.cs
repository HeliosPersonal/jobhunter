using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Claude.Ollama;

/// <summary>
/// The Ollama cheap-tier fallback behind the same <see cref="ILlmBatchClient"/> port (SAD §3, ADR-0005).
/// Selection is a configuration decision (<c>Llm:Provider = Ollama</c>), not a fork in the pipeline: the
/// orchestrator, the cost gate and the result-processing handler are all unchanged — they submit, poll and
/// stream through the same three methods.
///
/// <para>Ollama has no server-side batch API, so the adapter <em>synthesises</em> the batch lifecycle. At
/// <see cref="SubmitAsync"/> it runs each item synchronously against <c>/api/chat</c> — with the enrichment
/// schema bound through Ollama's <c>format</c> field, so the output shape is identical to the Anthropic
/// tier — and stores the per-item outcomes under a generated batch id. <see cref="GetStatusAsync"/> then
/// reports <see cref="ProviderBatchState.Ended"/> immediately and <see cref="GetResultsAsync"/> streams the
/// stored items. A per-item transport fault is surfaced as <see cref="BatchResultItem.ProviderError"/>,
/// never thrown, matching the Anthropic adapter's contract exactly (QG-3), so one bad item is one recorded
/// failure rather than a failed batch.</para>
/// </summary>
public sealed class OllamaBatchClient : ILlmBatchClient
{
    /// <summary>The named <see cref="HttpClient"/> the adapter resolves; Infrastructure wires resilience onto it.</summary>
    public const string ClientName = "ollama-chat";

    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly IOllamaResultStore _store;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly ILogger<OllamaBatchClient> _logger;

    internal OllamaBatchClient(
        HttpClient http,
        IOptions<OllamaOptions> options,
        IOllamaResultStore store,
        IClock clock,
        IIdGenerator ids,
        ILogger<OllamaBatchClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _options = options.Value;
        _store = store;
        _clock = clock;
        _ids = ids;
        _logger = logger;
    }

    public async Task<string> SubmitAsync(BatchSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var providerBatchId = "ollama-batch-" + _ids.NewId().ToString("N", CultureInfo.InvariantCulture);
        var createdAt = _clock.UtcNow;

        var results = new List<BatchResultItem>(submission.Items.Count);
        foreach (var item in submission.Items)
        {
            results.Add(await RunItemAsync(item, cancellationToken).ConfigureAwait(false));
        }

        _store.Save(providerBatchId, createdAt, results);

        _logger.LogInformation(
            "Ran Ollama fallback batch {ItemCount} items, model {Model}, prompt {PromptVersion}, batch {ProviderBatchId}.",
            submission.Items.Count, _options.Model, submission.PromptVersion, providerBatchId);

        return providerBatchId;
    }

    public Task<BatchStatus> GetStatusAsync(string providerBatchId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerBatchId);

        if (!_store.TryGet(providerBatchId, out var items))
        {
            // A batch this process did not run (a restart lost the in-memory store): report expired so the
            // poller carries the items over to the next Run rather than waiting forever. Availability, not
            // quality, is what the fallback guarantees (SAD §3).
            return Task.FromResult(new BatchStatus(ProviderBatchState.Expired, 0, 0, 0));
        }

        var succeeded = items.Count(i => i.ProviderError is null);
        var errored = items.Count - succeeded;
        return Task.FromResult(new BatchStatus(ProviderBatchState.Ended, succeeded, errored, 0));
    }

    public async IAsyncEnumerable<BatchResultItem> GetResultsAsync(
        string providerBatchId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerBatchId);

        _store.TryGet(providerBatchId, out var items);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    public Task<IReadOnlyList<ProviderBatchRef>> ListRecentBatchesAsync(
        DateTimeOffset createdOnOrAfter,
        CancellationToken cancellationToken) =>
        Task.FromResult(_store.ListSince(createdOnOrAfter));

    private async Task<BatchResultItem> RunItemAsync(BatchRequestItem item, CancellationToken cancellationToken)
    {
        var body = OllamaRequestBuilder.BuildChatBody(item, _options.Model, _options.MaxOutputTokens);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.BaseUrl), "/api/chat"));
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // A transport fault on one item is a recorded per-item failure, not a thrown batch: the item
            // carries over and retries next Run (AC-08), and the Run still advances (QG-3). The endpoint
            // and message are safe to surface; no secret crosses this boundary (invariant 12).
            _logger.LogWarning(ex, "Ollama item {CustomId} failed at the transport level; recording as a provider error.", item.CustomId);
            return new BatchResultItem(item.CustomId, null, $"Ollama transport fault: {ex.Message}", TokenUsage.Zero);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new BatchResultItem(
                    item.CustomId, null,
                    $"Ollama chat failed with status {(int)response.StatusCode} ({response.StatusCode}).",
                    TokenUsage.Zero);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return OllamaResponseParser.ParseChatResponse(item.CustomId, content);
        }
    }
}
