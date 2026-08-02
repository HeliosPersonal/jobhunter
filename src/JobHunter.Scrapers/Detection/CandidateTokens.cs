using JobHunter.Domain.Companies;

namespace JobHunter.Scrapers.Detection;

/// <summary>One board token to probe, and whether it was derived exactly from the company domain.</summary>
public sealed record CandidateToken(string Token, bool DerivedFromDomainExactly);

/// <summary>
/// Derives the board tokens worth probing for a company (contract §Detection probes). The domain's
/// registrable label yields the "bare name, hyphenated, concatenated" forms — <c>acme-corp.com</c> gives
/// <c>acme-corp</c> and <c>acmecorp</c> — and are flagged as domain-derived so an exact match scores the
/// small bonus. A careers URL that already points at a known board host contributes the token in its path,
/// which is the strongest hint of all but is not itself "derived from the domain".
/// </summary>
public static class CandidateTokens
{
    /// <summary>
    /// Returns the distinct candidate tokens for <paramref name="domain"/> and optional
    /// <paramref name="careersUrl"/>, domain-derived ones first, never empty for a valid domain.
    /// </summary>
    public static IReadOnlyList<CandidateToken> Derive(CanonicalDomain domain, string? careersUrl)
    {
        ArgumentNullException.ThrowIfNull(domain);

        var label = RegistrableLabel(domain.Value);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tokens = new List<CandidateToken>();

        void Add(string token, bool exact)
        {
            if (!string.IsNullOrEmpty(token) && seen.Add(token))
            {
                tokens.Add(new CandidateToken(token, exact));
            }
        }

        // Domain-derived forms: the label as-is (hyphenated) and with hyphens removed (concatenated).
        Add(label, exact: true);
        Add(label.Replace("-", string.Empty, StringComparison.Ordinal), exact: true);

        // A careers URL path segment is a strong provider hint but is not a domain derivation.
        foreach (var pathToken in CareersUrlTokens(careersUrl))
        {
            Add(pathToken, exact: false);
        }

        return tokens;
    }

    private static string RegistrableLabel(string registrableDomain)
    {
        var dot = registrableDomain.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? registrableDomain[..dot] : registrableDomain;
    }

    private static IEnumerable<string> CareersUrlTokens(string? careersUrl)
    {
        if (string.IsNullOrWhiteSpace(careersUrl)
            || !Uri.TryCreate(careersUrl, UriKind.Absolute, out var uri))
        {
            yield break;
        }

        // The last non-empty path segment of a board URL is the token, e.g.
        // https://boards.greenhouse.io/acme/... → "acme".
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
        {
            yield return segments[0];
        }
    }
}
