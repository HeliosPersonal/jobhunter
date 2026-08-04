using System.Net;
using JobHunter.Application.Abstractions;
using JobHunter.Application.Reporting;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Infrastructure.Http;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

/// <summary>
/// T04: apply-link verification classifies each destination by value (F5 SAD §11 D3, AC-11). It probes
/// through the <em>real</em> politeness handler in front of a fake transport, so the test proves the whole
/// promise — a robots-disallowed URL comes back <see cref="ApplyLinkStatus.Unverified"/> because the
/// production handler refused it, not because the test simulated a refusal. The classification the feature
/// turns on: a 2xx is reachable; a definitive 4xx/5xx or a DNS/transport failure is confirmed-unreachable
/// and drops the card; and everything inconclusive — our 5 s timeout, a rate deferral, a robots refusal, a
/// host that will not answer HEAD — is unverified, keeping the card with the "link unverified" flag. A
/// timeout is <em>never</em> confirmed-unreachable (D3). Zero network: the transport is a stub.
/// </summary>
public sealed class ApplyLinkVerifierTests
{
    private const string ApplyUrl = "https://apply.example.com/jobs/42";

    private static ApplyVerificationOptions Options(TimeSpan? timeout = null) =>
        new() { Timeout = timeout ?? TimeSpan.FromSeconds(5), MaxParallelism = 8 };

    /// <summary>
    /// Builds the verifier over the real <see cref="PolitenessHandler"/> chained to <paramref name="transport"/>,
    /// exactly as production wires the named client — so robots, SSRF and the rate budget all apply to the probe.
    /// </summary>
    private static ApplyLinkVerifier Build(
        HttpMessageHandler transport,
        ApplyVerificationOptions? options = null,
        IRobotsPolicy? robots = null,
        IRateLimiter? rateLimiter = null)
    {
        robots ??= AlwaysAllows();
        rateLimiter ??= AlwaysGrants();
        var ssrf = new SsrfGuard((_, _) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("8.8.8.8")]));
        var politenessOptions = Microsoft.Extensions.Options.Options.Create(new PolitenessOptions
        {
            UserAgent = "JobHunter/1.0 (+https://github.com/jobhunter/jobhunter; contact@x)",
        });

        var politeness = new PolitenessHandler(
            rateLimiter, robots, ssrf, new FakeClock(), politenessOptions,
            NullLogger<PolitenessHandler>.Instance)
        {
            InnerHandler = transport,
        };
        var client = new HttpClient(politeness);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PoliteHttp.ClientName).Returns(client);

        return new ApplyLinkVerifier(factory, options ?? Options(), NullLogger<ApplyLinkVerifier>.Instance);
    }

    private static IRobotsPolicy AlwaysAllows()
    {
        var robots = Substitute.For<IRobotsPolicy>();
        robots.IsAllowedAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        return robots;
    }

    private static IRateLimiter AlwaysGrants()
    {
        var limiter = Substitute.For<IRateLimiter>();
        limiter.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(RateLease.Allow);
        return limiter;
    }

    private static Task<ApplyLinkStatus> VerifyStatus(HttpStatusCode status)
    {
        var verifier = Build(StubTransport.Responds(_ => new HttpResponseMessage(status)));
        return verifier.VerifyAsync(ApplyUrl, CancellationToken.None);
    }

    // ---- reachable ----------------------------------------------------------------------------

    [Fact]
    public async Task A_success_is_reachable()
    {
        (await VerifyStatus(HttpStatusCode.OK)).ShouldBe(ApplyLinkStatus.Reachable);
    }

    // ---- confirmed-unreachable: definitive status and transport failure -----------------------

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_definitive_error_status_is_confirmed_unreachable(HttpStatusCode status)
    {
        (await VerifyStatus(status)).ShouldBe(ApplyLinkStatus.ConfirmedUnreachable);
    }

    [Fact]
    public async Task A_dns_or_transport_failure_is_confirmed_unreachable()
    {
        // The transport throwing HttpRequestException is how a DNS failure or a connection reset surfaces.
        var verifier = Build(StubTransport.Responds(
            _ => throw new HttpRequestException("no such host")));

        var status = await verifier.VerifyAsync(ApplyUrl, CancellationToken.None);

        status.ShouldBe(ApplyLinkStatus.ConfirmedUnreachable);
    }

    // ---- unverified: timeout, robots, rate, and HEAD-hostile statuses -------------------------

    [Fact]
    public async Task A_timeout_is_unverified_not_unreachable()
    {
        // A tiny probe budget against a transport that delays past it: our own CancelAfter fires. A slow host
        // must never be mistaken for a closed job (D3).
        var verifier = Build(
            StubTransport.Delays(TimeSpan.FromSeconds(30)),
            Options(timeout: TimeSpan.FromMilliseconds(50)));

        var status = await verifier.VerifyAsync(ApplyUrl, CancellationToken.None);

        status.ShouldBe(ApplyLinkStatus.Unverified);
    }

    [Fact]
    public async Task A_robots_disallowed_url_is_unverified_not_unreachable()
    {
        var robots = Substitute.For<IRobotsPolicy>();
        robots.IsAllowedAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        // The transport must never be reached — the handler refuses before any I/O.
        var verifier = Build(
            StubTransport.Responds(_ => throw new InvalidOperationException("robots refusal must precede the fetch")),
            robots: robots);

        var status = await verifier.VerifyAsync(ApplyUrl, CancellationToken.None);

        status.ShouldBe(ApplyLinkStatus.Unverified);
    }

    [Fact]
    public async Task A_rate_deferral_is_unverified()
    {
        var limiter = Substitute.For<IRateLimiter>();
        limiter.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RateLease.Deny(TimeSpan.FromSeconds(60)));
        var verifier = Build(
            StubTransport.Responds(_ => throw new InvalidOperationException("a deferred probe must not fetch")),
            rateLimiter: limiter);

        var status = await verifier.VerifyAsync(ApplyUrl, CancellationToken.None);

        status.ShouldBe(ApplyLinkStatus.Unverified);
    }

    [Theory]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.NotImplemented)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_head_hostile_or_transient_status_is_unverified(HttpStatusCode status)
    {
        // A host that will not answer HEAD (405/501) or asks us to back off (408/429) tells us nothing about
        // whether the opening is open — inconclusive, so the card is kept and flagged rather than dropped.
        (await VerifyStatus(status)).ShouldBe(ApplyLinkStatus.Unverified);
    }

    // ---- boundary and propagation -------------------------------------------------------------

    [Fact]
    public async Task A_non_absolute_url_is_unverified()
    {
        var verifier = Build(StubTransport.Responds(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var status = await verifier.VerifyAsync("/relative/path", CancellationToken.None);

        status.ShouldBe(ApplyLinkStatus.Unverified);
    }

    [Fact]
    public async Task An_insecure_scheme_is_unverified()
    {
        // The politeness handler refuses http:// before any I/O; that refusal is a probe limitation, not a
        // confirmed closure.
        var verifier = Build(
            StubTransport.Responds(_ => throw new InvalidOperationException("an http target must be refused first")));

        var status = await verifier.VerifyAsync("http://apply.example.com/jobs/42", CancellationToken.None);

        status.ShouldBe(ApplyLinkStatus.Unverified);
    }

    [Fact]
    public async Task A_caller_cancellation_propagates_rather_than_reporting_unverified()
    {
        // When the assembly window itself aborts (the caller's token), that is a shutdown, not a slow link:
        // the verifier propagates the cancellation instead of swallowing it as Unverified.
        using var cts = new CancellationTokenSource();
        var verifier = Build(StubTransport.Delays(TimeSpan.FromSeconds(30)));

        var probe = verifier.VerifyAsync(ApplyUrl, cts.Token);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => probe);
    }

    [Fact]
    public async Task It_probes_with_head_through_the_polite_client()
    {
        HttpRequestMessage? seen = null;
        var verifier = Build(StubTransport.Responds(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        await verifier.VerifyAsync(ApplyUrl, CancellationToken.None);

        seen.ShouldNotBeNull();
        seen!.Method.ShouldBe(HttpMethod.Head);
        // The politeness handler stamped the honest User-Agent on the probe, proving it went through the gate.
        seen.Headers.UserAgent.ToString().ShouldContain("JobHunter");
    }

    /// <summary>A fake transport at the bottom of the real politeness chain. Zero network.</summary>
    private sealed class StubTransport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        /// <summary>Answers each request synchronously from <paramref name="responder"/> (may throw).</summary>
        public static StubTransport Responds(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            new((request, _) => Task.FromResult(responder(request)));

        /// <summary>Never answers within <paramref name="delay"/> — used to trip the verifier's own timeout.</summary>
        public static StubTransport Delays(TimeSpan delay) =>
            new(async (_, ct) =>
            {
                await Task.Delay(delay, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
