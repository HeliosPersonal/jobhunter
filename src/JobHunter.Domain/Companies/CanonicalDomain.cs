using System.Globalization;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Companies;

/// <summary>
/// A company's identity key: the registrable domain, normalised so a rebrand, an ATS migration or a
/// cosmetic URL difference never orphans its jobs (data-model §companies). Canonicalisation is:
/// lowercase, strip the scheme, strip credentials, strip the port and path, strip a leading
/// <c>www.</c>, drop a trailing dot, convert Unicode to punycode (IDNA), then reduce to the registrable
/// domain via the <see cref="PublicSuffixList"/>. So <c>stripe.com</c>, <c>www.stripe.com</c> and
/// <c>https://Stripe.com/careers</c> canonicalise identically, while <c>foo.github.io</c> and
/// <c>bar.github.io</c> stay distinct.
/// </summary>
public sealed class CanonicalDomain : ValueObject
{
    public static readonly Error Invalid =
        new("company.domain.invalid", "The value is not a canonicalisable domain.");

    private static readonly IdnMapping Idn = new();

    private CanonicalDomain(string value) => Value = value;

    /// <summary>The lowercased, punycode, registrable domain, e.g. <c>stripe.com</c>.</summary>
    public string Value { get; }

    /// <summary>
    /// Canonicalises <paramref name="raw"/>. Returns a failure (never throws) for anything that is not
    /// a domain with a registrable part — an empty string, a bare public suffix, or an IP address.
    /// </summary>
    public static Result<CanonicalDomain> TryCreate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Invalid;
        }

        var host = ExtractHost(raw.Trim());
        if (host is null)
        {
            return Invalid;
        }

        host = host.ToLowerInvariant().TrimEnd('.');

        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        if (host.Length == 0 || !host.Contains('.', StringComparison.Ordinal))
        {
            return Invalid;
        }

        // An IPv4 literal has no registrable domain; reject rather than mis-key a company on an address.
        if (IsIpv4Literal(host))
        {
            return Invalid;
        }

        string ascii;
        try
        {
            ascii = Idn.GetAscii(host);
        }
        catch (ArgumentException)
        {
            // Malformed IDN label (e.g. a stray combining char) — not a usable domain.
            return Invalid;
        }

        var registrable = PublicSuffixList.GetRegistrableDomain(ascii);
        return registrable is null
            ? Invalid
            : Result<CanonicalDomain>.Success(new CanonicalDomain(registrable));
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    // Reduce an arbitrary URL-or-host string to its host component without constructing a Uri (which
    // rejects many bare hosts). Strips scheme, userinfo, port, path, query and fragment.
    private static string? ExtractHost(string input)
    {
        var value = input;

        var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            value = value[(schemeIndex + 3)..];
        }

        // Cut off path / query / fragment.
        var cut = value.IndexOfAny(['/', '?', '#']);
        if (cut >= 0)
        {
            value = value[..cut];
        }

        // Strip credentials (user:pass@host).
        var at = value.LastIndexOf('@');
        if (at >= 0)
        {
            value = value[(at + 1)..];
        }

        // Strip a port. IPv6 literals in brackets are not valid company domains, so a lone ':' is a port.
        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            value = value[..colon];
        }

        return value.Length == 0 ? null : value;
    }

    private static bool IsIpv4Literal(string host)
    {
        var parts = host.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        return parts.All(p => byte.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }
}
