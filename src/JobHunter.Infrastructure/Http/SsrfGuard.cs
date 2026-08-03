using System.Net;
using System.Net.Sockets;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// The SSRF guard (security §4, invariant 10). Every fetch target must resolve to a public address;
/// private, loopback, link-local and other non-routable ranges are refused. This matters because F8
/// fetches URLs derived from model output and company websites — the one place an external party
/// influences what we request. The address classification is pure and unit-tested exhaustively; DNS
/// resolution is injected so a test never touches the network.
/// </summary>
internal sealed class SsrfGuard(SsrfGuard.ResolveHost? resolver = null)
{
    /// <summary>Resolves a host name to its addresses. Injected so tests never hit DNS.</summary>
    internal delegate Task<IReadOnlyList<IPAddress>> ResolveHost(string host, CancellationToken cancellationToken);

    private readonly ResolveHost _resolver = resolver ?? DefaultResolve;

    /// <summary>
    /// True when <paramref name="uri"/>'s host resolves only to public, routable addresses. An IP literal
    /// is classified directly; a name is resolved and every returned address must be public — a single
    /// private answer refuses the whole target (defence against DNS rebinding to a private range).
    /// </summary>
    public async Task<bool> IsPublicAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            return IsPublic(literal);
        }

        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await _resolver(uri.Host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // A host that does not resolve is not fetchable; treat as refused rather than throwing.
            return false;
        }

        return addresses.Count > 0 && addresses.All(IsPublic);
    }

    /// <summary>
    /// Classifies a single address as publicly routable. Rejects loopback (<c>127.0.0.0/8</c>, <c>::1</c>),
    /// private ranges (<c>10/8</c>, <c>172.16/12</c>, <c>192.168/16</c>, unique-local <c>fc00::/7</c>),
    /// link-local (<c>169.254/16</c>, <c>fe80::/10</c>), and the other non-routable blocks.
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                return IsPublic(address.MapToIPv4());
            }

            return IsPublicV6(address);
        }

        return IsPublicV4(address);
    }

    private static bool IsPublicV4(IPAddress address)
    {
        var b = address.GetAddressBytes();

        // 0.0.0.0/8 (this network) and 127.0.0.0/8 (loopback).
        if (b[0] is 0 or 127)
        {
            return false;
        }

        // 10.0.0.0/8 (private).
        if (b[0] == 10)
        {
            return false;
        }

        // 172.16.0.0/12 (private).
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
        {
            return false;
        }

        // 192.168.0.0/16 (private).
        if (b[0] == 192 && b[1] == 168)
        {
            return false;
        }

        // 169.254.0.0/16 (link-local) and 100.64.0.0/10 (carrier-grade NAT).
        if (b[0] == 169 && b[1] == 254)
        {
            return false;
        }

        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
        {
            return false;
        }

        // 255.255.255.255 (broadcast) and 224.0.0.0/4 (multicast).
        if (b[0] >= 224)
        {
            return false;
        }

        return true;
    }

    private static bool IsPublicV6(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast)
        {
            return false;
        }

        var b = address.GetAddressBytes();

        // fc00::/7 — unique local addresses (the IPv6 equivalent of RFC 1918).
        if ((b[0] & 0xFE) == 0xFC)
        {
            return false;
        }

        // :: (unspecified).
        return !address.Equals(IPAddress.IPv6Any);
    }

    private static async Task<IReadOnlyList<IPAddress>> DefaultResolve(string host, CancellationToken cancellationToken)
    {
        var entries = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        return entries;
    }
}
