using System.Net;
using JobHunter.Application.Abstractions;
using JobHunter.Application.Reporting;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using Microsoft.Extensions.Logging;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// Verifies a card's apply destination through the shared politeness-gated client (F5 SAD §11 D3, AC-11,
/// QG-2). It issues a <c>HEAD</c> — the cheapest probe that still exercises the URL — through
/// <see cref="PoliteHttp.ClientName"/>, so the User-Agent, HTTPS-only rule, SSRF guard, <c>robots.txt</c>
/// check and per-host rate budget all apply exactly as they do to any other fetch: verification cannot
/// circumvent politeness because it does not own an <see cref="HttpClient"/> (invariant 10).
///
/// <para>The classification is the whole point. A success is <see cref="ApplyLinkStatus.Reachable"/>. A
/// definitive 4xx/5xx or a DNS/transport failure (surfaced as <see cref="HttpRequestException"/>) is
/// <see cref="ApplyLinkStatus.ConfirmedUnreachable"/> — the destination is gone, so the card is dropped and
/// the job flagged for closure. Everything inconclusive is <see cref="ApplyLinkStatus.Unverified"/>: our own
/// 5 s timeout, a rate deferral, a <c>robots.txt</c> refusal, or a probe the host will not answer with
/// <c>HEAD</c> (405/501). A timeout is Unverified, never ConfirmedUnreachable (D3) — a slow host is not a
/// closed job, and dropping its card on a stopwatch would be the exact false negative the design forbids.</para>
///
/// <para>The 5 s bound is a linked <see cref="CancellationTokenSource"/>, not the caller's token: when it
/// fires we return Unverified, but when the <em>caller</em> cancels (the assembly window itself is aborting)
/// we propagate, so a genuine shutdown is never mistaken for a slow link.</para>
/// </summary>
internal sealed class ApplyLinkVerifier(
    IHttpClientFactory httpClientFactory,
    ApplyVerificationOptions options,
    ILogger<ApplyLinkVerifier> logger) : IApplyLinkVerifier
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory
        ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ApplyVerificationOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<ApplyLinkVerifier> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<ApplyLinkStatus> VerifyAsync(string applyUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applyUrl);

        // A URL we cannot even form an absolute HTTPS request from is unverifiable, not a dead job: the
        // politeness handler would refuse it anyway, and we never fabricate a closure from a malformed link.
        if (!Uri.TryCreate(applyUrl, UriKind.Absolute, out var uri))
        {
            _logger.LogInformation("Apply URL {ApplyUrl} is not an absolute URI; treating as unverified.", applyUrl);
            return ApplyLinkStatus.Unverified;
        }

        var client = _httpClientFactory.CreateClient(PoliteHttp.ClientName);

        // Bound the probe to our own short timeout, linked to the caller's token. When ours fires the link is
        // Unverified; when the caller's fires the assembly window itself is aborting and we propagate.
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.Timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);

            return Classify(response, applyUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled — the assembly window is aborting, not the link being slow. Propagate.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Our 5 s bound fired: the host is slow, which tells us nothing about whether the job is open.
            _logger.LogInformation("Apply URL {ApplyUrl} verification timed out; treating as unverified.", applyUrl);
            return ApplyLinkStatus.Unverified;
        }
        catch (HttpRequestException ex)
        {
            // DNS failure, connection reset, TLS failure: the destination did not answer at all. That is a
            // definitive unreachable — the same class the test-plan pairs with 404/410/5xx (AC-11).
            _logger.LogInformation(ex, "Apply URL {ApplyUrl} failed to connect; confirmed unreachable.", applyUrl);
            return ApplyLinkStatus.ConfirmedUnreachable;
        }
    }

    private ApplyLinkStatus Classify(HttpResponseMessage response, string applyUrl)
    {
        var status = response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            return ApplyLinkStatus.Reachable;
        }

        // The politeness handler declines some fetches with its own machine reasons. None of these means the
        // job is gone — we simply could not probe it — so each is Unverified, never a confirmed closure. A
        // robots-disallowed apply URL in particular is unverifiable, not unreachable (D3).
        if (status == HttpStatusCode.Forbidden && response.ReasonPhrase is
            "robots-disallowed" or "ssrf-private-address" or "insecure-scheme")
        {
            _logger.LogInformation(
                "Apply URL {ApplyUrl} could not be probed ({Reason}); treating as unverified.",
                applyUrl, response.ReasonPhrase);
            return ApplyLinkStatus.Unverified;
        }

        // A rate deferral, a server-side timeout, or a host that will not answer HEAD is a probe limitation,
        // not evidence the opening closed. Keep the card and flag it unverified rather than dropping a live job.
        if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
        {
            _logger.LogInformation(
                "Apply URL {ApplyUrl} answered {Status}; inconclusive, treating as unverified.",
                applyUrl, (int)status);
            return ApplyLinkStatus.Unverified;
        }

        // Any other definitive 4xx/5xx — a 404, a 410, a 500 — is the destination telling us it is gone.
        _logger.LogInformation(
            "Apply URL {ApplyUrl} answered {Status}; confirmed unreachable.", applyUrl, (int)status);
        return ApplyLinkStatus.ConfirmedUnreachable;
    }
}
