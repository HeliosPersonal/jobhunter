using System.Net;
using System.Net.Http.Headers;
using JobHunter.Domain.Abstractions;
using JobHunter.Infrastructure.Http;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

public sealed class PolitenessHandlerTests
{
    private const string UserAgent = "JobHunter/1.0 (+https://github.com/jobhunter/jobhunter; contact@x)";

    private static PolitenessOptions Options(long maxBytes = PolitenessOptions.DefaultMaxResponseBytes) => new()
    {
        UserAgent = UserAgent,
        MaxResponseBytes = maxBytes,
    };

    private static HttpClient Build(
        StubHandler inner,
        out FakeClock clock,
        IRateLimiter? rateLimiter = null,
        IRobotsPolicy? robots = null,
        SsrfGuard? ssrf = null,
        PolitenessOptions? options = null)
    {
        clock = new FakeClock();
        rateLimiter ??= AlwaysGrants();
        robots ??= AlwaysAllows();
        ssrf ??= new SsrfGuard((_, _) => Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("8.8.8.8")]));
        var opts = Microsoft.Extensions.Options.Options.Create(options ?? Options());

        var handler = new PolitenessHandler(
            rateLimiter, robots, ssrf, clock, opts, NullLogger<PolitenessHandler>.Instance)
        {
            InnerHandler = inner,
        };
        return new HttpClient(handler);
    }

    private static IRateLimiter AlwaysGrants()
    {
        var limiter = Substitute.For<IRateLimiter>();
        limiter.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(RateLease.Allow);
        return limiter;
    }

    private static IRobotsPolicy AlwaysAllows()
    {
        var robots = Substitute.For<IRobotsPolicy>();
        robots.IsAllowedAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        return robots;
    }

    [Fact]
    public async Task A_permitted_request_reaches_the_inner_handler_with_our_user_agent()
    {
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Build(inner, out _);

        var response = await client.GetAsync(new Uri("https://boards.example/jobs"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.LastRequest.ShouldNotBeNull();
        inner.LastRequest!.Headers.UserAgent.ToString().ShouldContain("JobHunter");
    }

    [Fact]
    public async Task A_non_https_target_is_refused_before_any_io()
    {
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Build(inner, out _);

        var response = await client.GetAsync(new Uri("http://boards.example/jobs"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.ReasonPhrase.ShouldBe("insecure-scheme");
        inner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_private_address_target_is_refused_before_any_io()
    {
        var ssrf = new SsrfGuard((_, _) => Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("10.0.0.5")]));
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Build(inner, out _, ssrf: ssrf);

        var response = await client.GetAsync(new Uri("https://rebind.example/jobs"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.ReasonPhrase.ShouldBe("ssrf-private-address");
        inner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_robots_disallowed_path_is_refused_and_never_fetched()
    {
        var robots = Substitute.For<IRobotsPolicy>();
        robots.IsAllowedAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Build(inner, out _, robots: robots);

        var response = await client.GetAsync(new Uri("https://boards.example/private"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.ReasonPhrase.ShouldBe("robots-disallowed");
        inner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_exhausted_rate_budget_defers_rather_than_dropping()
    {
        var limiter = Substitute.For<IRateLimiter>();
        limiter.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RateLease.Deny(TimeSpan.FromSeconds(30)));
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Build(inner, out _, rateLimiter: limiter);

        var response = await client.GetAsync(new Uri("https://boards.example/jobs"));

        response.StatusCode.ShouldBe(PolitenessHandler.DeferredStatusCode);
        response.ReasonPhrase.ShouldBe("rate-deferred");
        response.Headers.RetryAfter!.Delta!.Value.ShouldBe(TimeSpan.FromSeconds(30));
        inner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_retry_after_delta_penalises_the_host_by_that_exact_duration()
    {
        var limiter = AlwaysGrants();
        var inner = new StubHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return r;
        });
        using var client = Build(inner, out _, rateLimiter: limiter);

        await client.GetAsync(new Uri("https://boards.example/jobs"));

        await limiter.Received(1).PenaliseAsync(
            "boards.example", TimeSpan.FromSeconds(120), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_retry_after_http_date_is_penalised_relative_to_the_clock()
    {
        var limiter = AlwaysGrants();
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = Build(inner, out var clock, rateLimiter: limiter);
        var retryAt = clock.UtcNow.AddSeconds(90);
        inner.Configure(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            r.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);
            return r;
        });

        await client.GetAsync(new Uri("https://boards.example/jobs"));

        await limiter.Received(1).PenaliseAsync(
            "boards.example",
            Arg.Is<TimeSpan>(t => t > TimeSpan.FromSeconds(80) && t <= TimeSpan.FromSeconds(90)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_successful_response_without_retry_after_applies_no_penalty()
    {
        var limiter = AlwaysGrants();
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Build(inner, out _, rateLimiter: limiter);

        await client.GetAsync(new Uri("https://boards.example/jobs"));

        await limiter.DidNotReceive().PenaliseAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_response_declaring_a_length_over_the_cap_is_abandoned_before_buffering()
    {
        var inner = new StubHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ThrowingStream()),
            };
            r.Content.Headers.ContentLength = 50L * 1024 * 1024;
            return r;
        });
        using var client = Build(inner, out _, options: Options(maxBytes: 10L * 1024 * 1024));

        var response = await client.GetAsync(new Uri("https://boards.example/jobs"),
            HttpCompletionOption.ResponseHeadersRead);

        // The oversized body was replaced with an empty capped body: reading it never touches the source.
        var body = await response.Content.ReadAsByteArrayAsync();
        body.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_body_streaming_over_the_cap_aborts_the_read()
    {
        // A chunked body with no declared Content-Length: the cap can only be enforced by streaming.
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnsizedStream(4096)),
        });
        using var client = Build(inner, out _, options: Options(maxBytes: 1024));

        var response = await client.GetAsync(new Uri("https://boards.example/jobs"),
            HttpCompletionOption.ResponseHeadersRead);

        await Should.ThrowAsync<ResponseTooLargeException>(() => response.Content.ReadAsByteArrayAsync());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        public int Calls { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        public void Configure(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    // A forward-only stream of a fixed number of bytes that reports no length, so StreamContent sends it
    // chunked (no Content-Length). This is the case the streaming cap exists to catch.
    private sealed class UnsizedStream(int totalBytes) : Stream
    {
        private int _remaining = totalBytes;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var n = Math.Min(count, _remaining);
            Array.Clear(buffer, offset, n);
            _remaining -= n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("The oversized body must never be read.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
