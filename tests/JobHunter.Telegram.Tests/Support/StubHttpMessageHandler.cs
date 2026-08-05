using System.Net;

namespace JobHunter.Telegram.Tests.Support;

/// <summary>
/// Replays canned responses for the notifier and long-poll suites — the whole of "zero network" (testing
/// conventions). It records every request (method, URI, body) so a test can assert the adapter built the
/// right request and that the bot token never left the base address, and it counts calls so a 429-retry
/// test can assert exactly how many attempts were made. A per-call factory lets a test return a 429 then a
/// 200 to drive the pacing-and-retry path.
/// </summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
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

        _requests.Add(new RecordedRequest(request.Method, request.RequestUri, body));

        var response = respond(request, _requests.Count);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    public sealed record RecordedRequest(HttpMethod Method, Uri? Uri, string? Body);
}
