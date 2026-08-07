using System.Net;
using JobHunter.Application.Abstractions;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Research;
using JobHunter.Infrastructure.Http;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

/// <summary>
/// The SSRF suite (SAD §10 QG-3, §11 D1, test-plan §The SSRF suite) — the reason this feature needs a
/// security review. Adversarial targets are pushed through the <em>full</em> guarded fetch path: the real
/// <see cref="PolitenessHandler"/> (which does the scheme and public-address checks) in front of a stub
/// transport, with <see cref="GuardedResearchFetch"/> adding the category allowlist and manual,
/// re-validating redirect following. Every refusal case asserts the request was <b>not made</b> — the stub
/// transport never saw the forbidden URL — not merely that a response was discarded. The "resolve once,
/// connect to the resolved address" guarantee that closes the DNS rebinding window is asserted separately in
/// <see cref="ResearchConnectorTests"/>, since that pin lives at the socket, below this path.
/// </summary>
public sealed class GuardedResearchFetchTests
{
    private const string Company = "stripe.com";
    private const string UserAgent = "JobHunter/1.0 (+https://github.com/jobhunter/jobhunter; contact@x)";

    /// <summary>
    /// Builds the fetch over the real politeness handler chained to <paramref name="transport"/>, exactly as
    /// production wires the research client — so the scheme and public-address checks are the production ones,
    /// not simulated. <paramref name="resolves"/> maps a host to the addresses the SSRF guard sees for it.
    /// </summary>
    private static GuardedResearchFetch Build(
        RecordingTransport transport,
        Dictionary<string, string[]>? resolves = null,
        IRateLimiter? rateLimiter = null)
    {
        var ssrf = new SsrfGuard((host, _) =>
        {
            var ips = resolves is not null && resolves.TryGetValue(host, out var mapped)
                ? mapped
                : ["93.184.216.34"]; // a public default so an unmapped host is not accidentally refused
            return Task.FromResult<IReadOnlyList<IPAddress>>(ips.Select(IPAddress.Parse).ToArray());
        });

        var robots = Substitute.For<IRobotsPolicy>();
        robots.IsAllowedAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        if (rateLimiter is null)
        {
            rateLimiter = Substitute.For<IRateLimiter>();
            rateLimiter.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(RateLease.Allow);
        }

        var options = Microsoft.Extensions.Options.Options.Create(new PolitenessOptions { UserAgent = UserAgent });
        var politeness = new PolitenessHandler(
            rateLimiter, robots, ssrf, new FakeClock(), options, NullLogger<PolitenessHandler>.Instance)
        {
            InnerHandler = transport,
        };
        var client = new HttpClient(politeness);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PoliteHttp.ResearchClientName).Returns(client);

        return new GuardedResearchFetch(factory, NullLogger<GuardedResearchFetch>.Instance);
    }

    private static Task<ResearchFetchResult> Fetch(
        GuardedResearchFetch fetch, string url, ResearchCategory category = ResearchCategory.EngineeringBlog) =>
        fetch.FetchAsync(category, new Uri(url), Company, CancellationToken.None);

    // --- Refused at the scheme check, before any I/O ------------------------------------------------------

    [Theory]
    [InlineData("http://blog.stripe.com/eng")]   // http, not https
    [InlineData("ftp://blog.stripe.com/eng")]
    [InlineData("file:///etc/passwd")]
    public async Task A_non_https_scheme_is_refused_at_the_scheme_check_without_a_request(string url)
    {
        var transport = new RecordingTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var fetch = Build(transport);

        var result = await Fetch(fetch, url);

        result.Outcome.ShouldBe(ResearchFetchOutcome.Refused);
        transport.Requests.ShouldBeEmpty();
    }

    // --- Refused because the host is not on the category allowlist, before any I/O ------------------------

    [Theory]
    [InlineData("https://127.0.0.1/eng")]          // raw loopback literal
    [InlineData("https://2130706433/eng")]         // decimal-encoded loopback — Uri normalises to 127.0.0.1
    [InlineData("https://0x7f000001/eng")]         // hex-encoded loopback
    [InlineData("https://169.254.169.254/latest")] // cloud metadata literal
    [InlineData("https://[::1]/eng")]              // IPv6 loopback literal
    [InlineData("https://evil.example/eng")]       // a public host not on the allowlist
    public async Task A_non_allowlisted_host_is_refused_without_a_request(string url)
    {
        var transport = new RecordingTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var fetch = Build(transport);

        var result = await Fetch(fetch, url);

        result.Outcome.ShouldBe(ResearchFetchOutcome.Refused);
        transport.Requests.ShouldBeEmpty();
    }

    // --- Refused because an allowlisted host resolves to a non-public address (the SSRF guard) -------------

    [Theory]
    [InlineData("10.0.0.5")]           // private
    [InlineData("192.168.1.9")]        // private
    [InlineData("169.254.169.254")]    // cloud metadata
    [InlineData("::1")]                // IPv6 loopback
    [InlineData("fc00::1")]            // IPv6 unique-local
    public async Task An_allowlisted_host_resolving_to_a_private_address_is_refused_without_a_request(string ip)
    {
        var transport = new RecordingTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var fetch = Build(transport, new Dictionary<string, string[]> { ["blog.stripe.com"] = [ip] });

        var result = await Fetch(fetch, "https://blog.stripe.com/eng");

        result.Outcome.ShouldBe(ResearchFetchOutcome.Refused);
        result.Reason.ShouldBe("ssrf-private-address");
        transport.Requests.ShouldBeEmpty();
    }

    // --- A redirect from a public host into private space is refused AFTER the redirect --------------------

    [Fact]
    public async Task A_public_host_redirecting_into_private_space_is_refused_after_the_redirect()
    {
        // blog.stripe.com is public and allowlisted; it 302s to internal.stripe.com, also allowlisted, which
        // resolves to a private address. The redirect hop must be refused — and the private URL never fetched.
        var transport = new RecordingTransport(request =>
        {
            if (request.RequestUri!.Host == "blog.stripe.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("https://internal.stripe.com/admin");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var fetch = Build(transport, new Dictionary<string, string[]>
        {
            ["blog.stripe.com"] = ["93.184.216.34"],
            ["internal.stripe.com"] = ["10.0.0.7"],
        });

        var result = await Fetch(fetch, "https://blog.stripe.com/eng");

        result.Outcome.ShouldBe(ResearchFetchOutcome.Refused);
        result.Reason.ShouldBe("ssrf-private-address");
        // The public first hop was made; the private redirect target was refused before any request to it.
        transport.Requests.ShouldContain(u => u.Host == "blog.stripe.com");
        transport.Requests.ShouldNotContain(u => u.Host == "internal.stripe.com");
    }

    [Fact]
    public async Task A_redirect_to_a_non_allowlisted_host_is_refused_without_fetching_it()
    {
        var transport = new RecordingTransport(request =>
        {
            if (request.RequestUri!.Host == "blog.stripe.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("https://evil.example/pwn");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var fetch = Build(transport);

        var result = await Fetch(fetch, "https://blog.stripe.com/eng");

        result.Outcome.ShouldBe(ResearchFetchOutcome.Refused);
        transport.Requests.ShouldNotContain(u => u.Host == "evil.example");
    }

    // --- The permitted case: a public, allowlisted host is fetched ---------------------------------------

    [Fact]
    public async Task A_public_allowlisted_host_is_fetched_and_its_body_returned()
    {
        var transport = new RecordingTransport(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("engineering culture") });
        var fetch = Build(transport, new Dictionary<string, string[]> { ["blog.stripe.com"] = ["93.184.216.34"] });

        var result = await Fetch(fetch, "https://blog.stripe.com/eng");

        result.Outcome.ShouldBe(ResearchFetchOutcome.Ok);
        result.Body.ShouldBe("engineering culture");
        result.FinalUrl!.ToString().ShouldBe("https://blog.stripe.com/eng");
        transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_redirect_to_another_allowlisted_public_host_is_followed_to_the_body()
    {
        // github.com is the open-source allowlist; a redirect within it is followed and the final URL is what
        // a later claim would cite.
        var transport = new RecordingTransport(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/stripe")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirect.Headers.Location = new Uri("https://github.com/stripe/stripe-cli");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("repos") };
        });
        var fetch = Build(transport, new Dictionary<string, string[]> { ["github.com"] = ["140.82.113.3"] });

        var result = await fetch.FetchAsync(
            ResearchCategory.OpenSource, new Uri("https://github.com/stripe"), Company, CancellationToken.None);

        result.Outcome.ShouldBe(ResearchFetchOutcome.Ok);
        result.Body.ShouldBe("repos");
        result.FinalUrl!.ToString().ShouldBe("https://github.com/stripe/stripe-cli");
    }

    [Fact]
    public async Task A_redirect_loop_is_bounded_and_ends_as_an_http_error_not_a_hang()
    {
        var transport = new RecordingTransport(request =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri($"https://blog.stripe.com{request.RequestUri!.AbsolutePath}/x");
            return redirect;
        });
        var fetch = Build(transport, new Dictionary<string, string[]> { ["blog.stripe.com"] = ["93.184.216.34"] });

        var result = await Fetch(fetch, "https://blog.stripe.com/eng");

        result.Outcome.ShouldBe(ResearchFetchOutcome.HttpError);
    }

    [Fact]
    public async Task A_rate_deferral_surfaces_as_deferred_with_its_retry_after()
    {
        var retryAfter = TimeSpan.FromSeconds(30);
        var rateLimiter = Substitute.For<IRateLimiter>();
        rateLimiter.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RateLease.Deny(retryAfter));
        var transport = new RecordingTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var fetch = Build(transport, new Dictionary<string, string[]> { ["blog.stripe.com"] = ["93.184.216.34"] },
            rateLimiter);

        var result = await Fetch(fetch, "https://blog.stripe.com/eng");

        result.Outcome.ShouldBe(ResearchFetchOutcome.Deferred);
        result.RetryAfter.ShouldBe(retryAfter);
        // Deferred, not fetched: the rate budget is spent before the transport is reached.
        transport.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_non_success_status_is_an_http_error_not_a_refusal()
    {
        var transport = new RecordingTransport(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var fetch = Build(transport, new Dictionary<string, string[]> { ["blog.stripe.com"] = ["93.184.216.34"] });

        var result = await Fetch(fetch, "https://blog.stripe.com/eng");

        result.Outcome.ShouldBe(ResearchFetchOutcome.HttpError);
    }

    /// <summary>Records every request URI it is asked for, so a test can assert a URL was never fetched.</summary>
    private sealed class RecordingTransport(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
