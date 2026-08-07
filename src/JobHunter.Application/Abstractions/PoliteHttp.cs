namespace JobHunter.Application.Abstractions;

/// <summary>
/// The name of the shared, politeness-gated <see cref="System.Net.Http.HttpClient"/> that every ATS
/// adapter resolves from <see cref="System.Net.Http.IHttpClientFactory"/> (SAD §8, QG-2). It lives in
/// Application because both Infrastructure (which registers the client and attaches the politeness
/// handler) and Scrapers (which consume it) reference this layer, but neither references the other.
/// An adapter that asks for this client by name is polite by construction; it cannot build its own.
/// </summary>
public static class PoliteHttp
{
    /// <summary>The registered name of the gated outbound HTTP client.</summary>
    public const string ClientName = "jobhunter-polite";

    /// <summary>
    /// The registered name of the research-scoped gated client (F8 QG-3). It is the politeness pipeline
    /// plus a connection that resolves DNS once and dials the address it validated, closing the DNS
    /// rebinding window for the one feature whose fetch targets derive partly from model output.
    /// </summary>
    public const string ResearchClientName = "jobhunter-research-polite";
}
