using JobHunter.Application.Abstractions;

namespace JobHunter.Scrapers.Tests.Support;

/// <summary>
/// An <see cref="IHttpClientFactory"/> that hands back a client built on a caller-supplied handler under
/// the gated client's name, so an adapter resolving <see cref="PoliteHttp.ClientName"/> gets the stub.
/// The production politeness handler is unit-tested separately in the Infrastructure suite; here we prove
/// the adapter's parsing and streaming, not the handler.
/// </summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
