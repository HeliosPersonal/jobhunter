using System.Net;

namespace JobHunter.Claude.Tests.Support;

/// <summary>
/// Replays canned responses for the Anthropic adapter suites — the whole of "zero network" (testing
/// conventions). It records every request (method, URI, headers, body) so a test can assert the adapter
/// built the right request, and it counts calls so a retry test can assert exactly how many attempts were
/// made. A per-call response factory lets a test return a 500 then a 200 to drive the resilience handler.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public int CallCount => _requests.Count;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var headers = request.Headers
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);

        _requests.Add(new RecordedRequest(request.Method, request.RequestUri, headers, body));

        var response = respond(request, _requests.Count);
        response.RequestMessage = request;
        return response;
    }

    public static StubHttpMessageHandler Always(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => new HttpResponseMessage(status) { Content = new StringContent(body) });

    public sealed record RecordedRequest(
        HttpMethod Method,
        Uri? Uri,
        IReadOnlyDictionary<string, string> Headers,
        string? Body);
}
