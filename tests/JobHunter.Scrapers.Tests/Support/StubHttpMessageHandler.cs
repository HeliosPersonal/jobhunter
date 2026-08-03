using System.Net;

namespace JobHunter.Scrapers.Tests.Support;

/// <summary>
/// Replays a single canned response for every request — the whole of "zero network" for the adapter
/// suites. It records the URL it was asked for so a test can assert the adapter built the right board URL.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    public Uri? LastRequestUri { get; private set; }

    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        CallCount++;
        var response = respond(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public static StubHttpMessageHandler WithBody(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
}
