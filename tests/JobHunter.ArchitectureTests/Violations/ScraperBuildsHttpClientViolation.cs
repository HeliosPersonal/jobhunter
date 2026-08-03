namespace JobHunter.ArchitectureTests.Violations;

/// <summary>
/// A deliberately-violating fixture for QG-2: an ATS adapter that builds its own <see cref="HttpClient"/>
/// instead of being handed the shared, politeness-gated one. The QG-2 source scan must go red against
/// this, proving the guard is real. It lives under <c>tests</c>, never <c>src/JobHunter.Scrapers</c>, so
/// the production rule never sees it.
/// </summary>
internal sealed class ScraperBuildsHttpClientViolation
{
    public static HttpClient BuildOwnClient()
    {
        var handler = new SocketsHttpHandler();
        return new HttpClient(handler);
    }
}
