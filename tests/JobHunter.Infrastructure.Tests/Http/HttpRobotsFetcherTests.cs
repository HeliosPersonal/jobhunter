using System.Net;
using System.Text;
using JobHunter.Infrastructure.Http;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Http;

public sealed class HttpRobotsFetcherTests
{
    private static HttpRobotsFetcher Build(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new HttpClient(new StubHandler(responder));
        return new HttpRobotsFetcher(client, Options.Create(new PolitenessOptions()));
    }

    [Fact]
    public async Task A_200_returns_the_body_as_reachable()
    {
        var fetcher = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("User-agent: *\nDisallow: /x"),
        });

        var result = await fetcher.FetchAsync(new Uri("https://boards.example/robots.txt"), default);

        result.Reachable.ShouldBeTrue();
        result.Malformed.ShouldBeFalse();
        result.Body.ShouldContain("Disallow: /x");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_non_success_status_reads_as_unreachable(HttpStatusCode status)
    {
        var fetcher = Build(_ => new HttpResponseMessage(status));

        var result = await fetcher.FetchAsync(new Uri("https://boards.example/robots.txt"), default);

        result.Reachable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_declared_length_over_the_cap_reads_as_malformed()
    {
        var fetcher = Build(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("x") };
            r.Content.Headers.ContentLength = HttpRobotsFetcher.MaxRobotsBytes + 1;
            return r;
        });

        var result = await fetcher.FetchAsync(new Uri("https://boards.example/robots.txt"), default);

        result.Malformed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_body_over_the_cap_reads_as_malformed()
    {
        var big = new string('a', (int)HttpRobotsFetcher.MaxRobotsBytes + 10);
        var fetcher = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(big, Encoding.UTF8),
        });

        var result = await fetcher.FetchAsync(new Uri("https://boards.example/robots.txt"), default);

        result.Malformed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_connection_failure_reads_as_unreachable()
    {
        var fetcher = Build(_ => throw new HttpRequestException("connection refused"));

        var result = await fetcher.FetchAsync(new Uri("https://boards.example/robots.txt"), default);

        result.Reachable.ShouldBeFalse();
        result.Malformed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_request_timeout_reads_as_unreachable()
    {
        var fetcher = Build(_ => throw new TaskCanceledException("timeout"));

        var result = await fetcher.FetchAsync(new Uri("https://boards.example/robots.txt"), default);

        result.Reachable.ShouldBeFalse();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
