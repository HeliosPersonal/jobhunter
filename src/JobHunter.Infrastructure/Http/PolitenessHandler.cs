using System.Net;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// The one <see cref="DelegatingHandler"/> every outbound ATS fetch passes through (SAD §8, QG-2).
/// It sets the honest <c>User-Agent</c>, refuses non-HTTPS and SSRF targets, checks <c>robots.txt</c>,
/// consumes the per-host token budget, honours <c>Retry-After</c> exactly, and caps the response body at
/// 10 MB before it is buffered. An adapter is handed an <see cref="HttpClient"/> built on this handler
/// and cannot construct its own (enforced by an architecture test), so politeness is structural: a new
/// adapter is polite whether or not its author read this file.
/// </summary>
internal sealed class PolitenessHandler(
    IRateLimiter rateLimiter,
    IRobotsPolicy robotsPolicy,
    SsrfGuard ssrfGuard,
    IClock clock,
    IOptions<PolitenessOptions> options,
    ILogger<PolitenessHandler> logger) : DelegatingHandler
{
    /// <summary>The status a deferred (rate-limited) request reports so the caller requeues with delay.</summary>
    public const HttpStatusCode DeferredStatusCode = HttpStatusCode.TooManyRequests;

    private readonly PolitenessOptions _options = options.Value;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var uri = request.RequestUri
            ?? throw new InvalidOperationException("A politeness-gated request must have an absolute URI.");
        var host = uri.Host;

        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);

        // TLS only — an http:// target is a configuration error, refused before any I/O (security §4).
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Refused non-HTTPS fetch target for host {Host}", host);
            return Refused(request, "insecure-scheme");
        }

        if (!await ssrfGuard.IsPublicAsync(uri, cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Refused fetch to non-public address for host {Host}", host);
            return Refused(request, "ssrf-private-address");
        }

        if (!await robotsPolicy.IsAllowedAsync(uri, _options.UserAgent, cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("robots.txt disallows fetch of {Path} on host {Host}", uri.AbsolutePath, host);
            return Refused(request, "robots-disallowed");
        }

        var lease = await rateLimiter.AcquireAsync(host, cancellationToken).ConfigureAwait(false);
        if (!lease.Granted)
        {
            logger.LogInformation("Rate budget for host {Host} exhausted; deferring by {RetryAfter}",
                host, lease.RetryAfter);
            return Deferred(request, lease.RetryAfter);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Honour Retry-After exactly: record the penalty so the next attempt to this host waits it out,
        // and never override it with our own shorter backoff (AC-07).
        if (TryGetRetryAfter(response, out var retryAfter))
        {
            await rateLimiter.PenaliseAsync(host, retryAfter, cancellationToken).ConfigureAwait(false);
        }

        EnforceResponseSizeCap(response, host);
        return response;
    }

    private void EnforceResponseSizeCap(HttpResponseMessage response, string host)
    {
        // A declared length over the cap is rejected outright, before any body is buffered. A body with
        // no declared length is wrapped so streaming aborts the moment it crosses the cap.
        if (response.Content.Headers.ContentLength is { } declared && declared > _options.MaxResponseBytes)
        {
            logger.LogWarning("Response from host {Host} declares {Bytes} bytes, over the {Cap} cap; abandoning",
                host, declared, _options.MaxResponseBytes);
            response.Content = new CappedContent(EmptyContent(), _options.MaxResponseBytes);
            return;
        }

        response.Content = new CappedContent(response.Content, _options.MaxResponseBytes);
    }

    private bool TryGetRetryAfter(HttpResponseMessage response, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var header = response.Headers.RetryAfter;
        if (header is null)
        {
            return false;
        }

        if (header.Delta is { } delta && delta > TimeSpan.Zero)
        {
            retryAfter = delta;
            return true;
        }

        if (header.Date is { } date)
        {
            var wait = date - clock.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                retryAfter = wait;
                return true;
            }
        }

        return false;
    }

    private static HttpResponseMessage Refused(HttpRequestMessage request, string reason) =>
        new(HttpStatusCode.Forbidden)
        {
            RequestMessage = request,
            ReasonPhrase = reason,
            Content = EmptyContent(),
        };

    private static HttpResponseMessage Deferred(HttpRequestMessage request, TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(DeferredStatusCode)
        {
            RequestMessage = request,
            ReasonPhrase = "rate-deferred",
            Content = EmptyContent(),
        };

        if (retryAfter > TimeSpan.Zero)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
        }

        return response;
    }

    private static ByteArrayContent EmptyContent() => new([]);
}
