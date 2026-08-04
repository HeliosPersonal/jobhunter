using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Claude.Anthropic;

/// <summary>
/// The Anthropic Message Batches adapter behind <see cref="ILlmBatchClient"/> (SAD §5, ADR-0005). All
/// request building and response parsing lives in the pure <see cref="AnthropicRequestBuilder"/> and
/// <see cref="AnthropicResponseParser"/>, so the adapter itself is a thin HTTP shell and the whole thing
/// is asserted against saved payloads with zero network.
///
/// <para>Streaming matters: <see cref="GetResultsAsync"/> reads the JSONL result body line by line, so a
/// 150-item result set is processed without being materialised (SAD §5). Transport faults and 5xx retry
/// through the injected <see cref="HttpClient"/>'s resilience handler; a 4xx is a request error and is
/// surfaced as an exception without retry. The API key is set as a header and appears in no log, no span
/// and no exception message (invariant 12).</para>
/// </summary>
public sealed class AnthropicBatchClient : ILlmBatchClient
{
    /// <summary>The named <see cref="HttpClient"/> the adapter resolves; Infrastructure wires resilience onto it.</summary>
    public const string ClientName = "anthropic-batches";

    private readonly HttpClient _http;
    private readonly PricingOptions _pricing;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicBatchClient> _logger;

    public AnthropicBatchClient(
        HttpClient http,
        IOptions<PricingOptions> pricing,
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicBatchClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _pricing = pricing.Value;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SubmitAsync(BatchSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var modelId = _pricing.For(submission.Tier).ModelId;
        var body = AnthropicRequestBuilder.BuildSubmitBody(submission, modelId, _options.MaxOutputTokens);

        using var request = NewRequest(HttpMethod.Post, "/v1/messages/batches");
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "submit", cancellationToken).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var providerBatchId = AnthropicResponseParser.ParseBatchId(content);

        _logger.LogInformation(
            "Submitted enrichment batch {ItemCount} items, tier {Tier}, prompt {PromptVersion}, provider batch {ProviderBatchId}.",
            submission.Items.Count, submission.Tier, submission.PromptVersion, providerBatchId);

        return providerBatchId;
    }

    public async Task<BatchStatus> GetStatusAsync(string providerBatchId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerBatchId);

        using var request = NewRequest(HttpMethod.Get, $"/v1/messages/batches/{Uri.EscapeDataString(providerBatchId)}");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "status", cancellationToken).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return AnthropicResponseParser.ParseStatus(content);
    }

    public async Task<IReadOnlyList<ProviderBatchRef>> ListRecentBatchesAsync(
        DateTimeOffset createdOnOrAfter,
        CancellationToken cancellationToken)
    {
        // The list endpoint pages most-recent-first; one page is ample for reconciliation, which only cares
        // about batches created since the current Run began (SAD §11 D5). The created-on-or-after bound is
        // applied client-side because the batch list is small and the filter keeps the adapter provider-shaped.
        using var request = NewRequest(HttpMethod.Get, "/v1/messages/batches?limit=100");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "list", cancellationToken).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var all = AnthropicResponseParser.ParseBatchList(content);
        return all.Where(b => b.CreatedAt >= createdOnOrAfter).ToList();
    }

    public async IAsyncEnumerable<BatchResultItem> GetResultsAsync(
        string providerBatchId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerBatchId);

        using var request = NewRequest(
            HttpMethod.Get, $"/v1/messages/batches/{Uri.EscapeDataString(providerBatchId)}/results");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "results", cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // The parser maps a per-item provider error to a value, so one bad item never breaks the stream.
            yield return AnthropicResponseParser.ParseResultLine(line);
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(_options.BaseUrl), path));
        request.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", _options.ApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // The status code is safe to surface; the body may echo the request, so it is not put in the message
        // (invariant 12 — no secret, no key, no CV in a log or an exception). 4xx does not retry; the shared
        // resilience handler only retries transient transport and 5xx faults.
        var status = (int)response.StatusCode;
        _ = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new AnthropicApiException(
            $"Anthropic batch {operation} failed with status {status} ({response.StatusCode}).", response.StatusCode);
    }
}

/// <summary>
/// An infrastructure fault talking to the Anthropic API. It carries the HTTP status so a caller can tell a
/// client error (4xx, not retried) from a server error (5xx, retried by the resilience handler). It never
/// carries the request body, the API key or any prompt content (invariant 12). It derives from the
/// provider-agnostic <see cref="LlmBatchClientException"/> so a caller can catch the port's declared fault
/// without depending on this adapter (F5 T05's inline narrative fallback).
/// </summary>
public sealed class AnthropicApiException(string message, HttpStatusCode statusCode)
    : LlmBatchClientException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
