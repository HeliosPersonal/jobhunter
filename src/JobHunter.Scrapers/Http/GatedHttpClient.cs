using System.Net;
using JobHunter.Application.Abstractions;

namespace JobHunter.Scrapers.Http;

/// <summary>
/// The one way an adapter obtains an outbound response: it asks <see cref="IHttpClientFactory"/> for the
/// shared, politeness-gated client by name (QG-2). No adapter constructs an <see cref="HttpClient"/>, so
/// the user-agent, robots, SSRF, rate budget and size cap apply whether or not the adapter's author read
/// the politeness handler. A refusal (robots, SSRF, insecure scheme) and a rate deferral surface as
/// values on <see cref="GatedResponse"/>, not exceptions — acquisition of one board is one failure domain.
/// </summary>
public sealed class GatedHttpClient(IHttpClientFactory httpClientFactory)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory
        ?? throw new ArgumentNullException(nameof(httpClientFactory));

    /// <summary>Fetches <paramref name="url"/> through the gated client and classifies the outcome.</summary>
    public async Task<GatedResponse> GetAsync(string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var client = _httpClientFactory.CreateClient(PoliteHttp.ClientName);
        using var response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var status = response.StatusCode;

        // The politeness handler reports a rate deferral as 429 with a Retry-After it computed. The caller
        // requeues with that delay rather than treating it as a provider failure.
        if (status == HttpStatusCode.TooManyRequests && response.ReasonPhrase == "rate-deferred")
        {
            return GatedResponse.Deferred(response.Headers.RetryAfter?.Delta);
        }

        // A refusal is a 403 whose reason phrase is the machine reason the handler set. It is not a
        // provider error — it is us declining to fetch — so it is classified as robots/SSRF, never HTTP.
        if (status == HttpStatusCode.Forbidden && response.ReasonPhrase is { } reason
            && reason is "robots-disallowed" or "ssrf-private-address" or "insecure-scheme")
        {
            return GatedResponse.Refused(reason);
        }

        if (!response.IsSuccessStatusCode)
        {
            return GatedResponse.HttpError((int)status);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return GatedResponse.Ok((int)status, body);
    }
}
