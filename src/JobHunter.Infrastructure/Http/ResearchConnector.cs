using System.Net;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// The connection half of the research SSRF defence (SAD §11 D1, QG-3): "resolve once, connect to the
/// resolved address." It is the <c>ConnectCallback</c> of the research-scoped HTTP client, so it runs at
/// the moment a socket is opened — after every earlier check, and after any redirect. It resolves the
/// target host <em>once</em>, refuses unless <em>every</em> returned address is public, and dials the exact
/// address it validated. Closing the gap between the address checked and the address dialed is what defeats
/// DNS rebinding: a name that answers public to an earlier validation but private at connect time never gets
/// a socket, because this callback re-resolves once here and connects only to a public answer.
/// </summary>
internal sealed class ResearchConnector
{
    /// <summary>Opens a transport stream to an already-validated public <paramref name="address"/>.</summary>
    internal delegate Task<Stream> Dial(IPAddress address, int port, CancellationToken cancellationToken);

    private readonly SsrfGuard.ResolveHost _resolver;
    private readonly Dial _dial;

    public ResearchConnector(SsrfGuard.ResolveHost resolver, Dial dial)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _dial = dial ?? throw new ArgumentNullException(nameof(dial));
    }

    /// <summary>
    /// Resolves <paramref name="host"/> once, refuses unless every resolved address is public, and dials the
    /// validated address. Throws <see cref="HttpRequestException"/> — surfaced to the caller as a transport
    /// failure — when the host resolves to any non-public address or does not resolve, so no socket is opened
    /// to a private range even if an earlier hop's validation was raced (SAD §11 D1). An IP literal is
    /// classified directly with no resolution: there is no rebinding window for an address that is fixed.
    /// </summary>
    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        IReadOnlyList<IPAddress> addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await _resolver(host, cancellationToken).ConfigureAwait(false);

        if (addresses.Count == 0 || !addresses.All(SsrfGuard.IsPublic))
        {
            // No socket is opened. A private or unresolvable answer at connect time is refused here, so the
            // rebinding attack (public at validation, private at connect) never reaches a private address.
            throw new HttpRequestException("ssrf-private-address");
        }

        // Connect to the exact address just validated — never a fresh resolution the connection layer might
        // otherwise perform, which is the gap DNS rebinding exploits.
        return await _dial(addresses[0], port, cancellationToken).ConfigureAwait(false);
    }
}
