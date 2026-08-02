using System.Net;

namespace JobHunter.Scrapers.Tests.Support;

/// <summary>
/// A zero-network handler that answers each probe URL from a routing function, so a detection test can
/// model "this provider responds for this token, everything else 404s" without recording live traffic.
/// The function returns <c>null</c> for a URL that should 404 (a non-responding provider/token).
/// </summary>
internal sealed class RoutingHttpMessageHandler(Func<Uri, string?> route) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = route(request.RequestUri!);
        var response = body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
