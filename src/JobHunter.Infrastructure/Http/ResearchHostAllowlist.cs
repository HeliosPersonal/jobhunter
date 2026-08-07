using JobHunter.Domain.Research;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// The category host allowlist (SAD §8 Allowlist, QG-3, risk D1) — the second half of the SSRF defence
/// beside <see cref="SsrfGuard"/>'s public-address check. A research fetch target must both resolve to a
/// public address <em>and</em> match its category's host pattern; anything else is refused and logged.
/// This matters because F8 is the one feature whose fetch targets derive partly from model output and
/// company-controlled pages, so the set of hosts we will ever contact is pinned here rather than trusted.
///
/// <para>Two shapes of rule. A <em>company-scoped</em> category (the engineering blog, the stack, the
/// interview process) permits only the company's own registrable domain and its subdomains — the evidence
/// for those lives on the company's own site. A <em>third-party</em> category permits a fixed set of public
/// hosts (open source is GitHub). Matching is on a dot boundary, so <c>evil-github.com</c> and
/// <c>stripe.com.evil.test</c> never match. A category with no configured hosts is refused by default —
/// absence is a deny, never an allow-all.</para>
/// </summary>
internal static class ResearchHostAllowlist
{
    // Categories whose evidence lives on the company's own web presence: permit the company domain only.
    private static readonly HashSet<ResearchCategory> CompanyScoped =
    [
        ResearchCategory.EngineeringBlog,
        ResearchCategory.Stack,
        ResearchCategory.InterviewProcess,
    ];

    // Third-party categories: a fixed set of registrable hosts we will contact for that category. A category
    // absent here (Funding, Reviews, News, Layoffs until their sources are chosen in T04) is refused — a safe
    // default that surfaces the category as unavailable rather than reaching an unvetted host.
    private static readonly Dictionary<ResearchCategory, string[]> ThirdParty =
        new()
        {
            [ResearchCategory.OpenSource] = ["github.com"],
        };

    /// <summary>
    /// Whether <paramref name="uri"/>'s host is permitted for <paramref name="category"/> when researching a
    /// company whose canonical domain is <paramref name="companyDomain"/>. A company-scoped category matches
    /// the company domain and its subdomains; a third-party category matches its configured hosts. All
    /// matching is on a dot boundary and case-insensitive.
    /// </summary>
    public static bool IsAllowed(ResearchCategory category, Uri uri, string companyDomain)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var host = uri.Host;

        if (CompanyScoped.Contains(category))
        {
            return !string.IsNullOrWhiteSpace(companyDomain) && MatchesDomain(host, companyDomain.Trim());
        }

        return ThirdParty.TryGetValue(category, out var hosts)
            && hosts.Any(allowed => MatchesDomain(host, allowed));
    }

    /// <summary>
    /// True when <paramref name="host"/> equals <paramref name="domain"/> or is a subdomain of it, matched on
    /// a dot boundary so a suffix that is not a label boundary (e.g. <c>notgithub.com</c> against
    /// <c>github.com</c>) never matches.
    /// </summary>
    private static bool MatchesDomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
