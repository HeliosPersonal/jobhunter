using System.Net;

namespace JobHunter.Search.Tests.Support;

/// <summary>
/// A zero-network <see cref="HttpMessageHandler"/> that answers each request from a supplied function and
/// records what it was asked, so an <see cref="JobHunter.Search.TypesenseIndexer"/> test can assert both
/// the outcome mapping and the exact request shape (method, path, body) without a Typesense instance. A
/// null response function models a transport failure — it throws <see cref="HttpRequestException"/>, the
/// "index unreachable" case (QG-3).
/// </summary>
internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, string, HttpResponseMessage>? responder = null) : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage>? _responder = responder;

    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.TryGetValues("X-TYPESENSE-API-KEY", out var keys) ? string.Join(",", keys) : null));

        if (_responder is null)
        {
            throw new HttpRequestException("Simulated transport failure: the index is unreachable.");
        }

        return _responder(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body = "{}") =>
        new(status) { Content = new StringContent(body) };
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, string? ApiKey);
