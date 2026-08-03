using System.Collections.Frozen;

namespace JobHunter.Domain.Companies;

/// <summary>
/// A compact <a href="https://publicsuffix.org/">Public Suffix List</a> matcher, used by
/// <see cref="CanonicalDomain"/> to reduce a host to its registrable domain — the point at which two
/// subdomains belong to the same organisation. <c>careers.stripe.com</c> and <c>stripe.com</c> reduce
/// to <c>stripe.com</c> (one company); <c>foo.github.io</c> and <c>bar.github.io</c> do not reduce
/// further because <c>github.io</c> is itself a public suffix (two companies).
///
/// The list embedded here is a curated subset — the ICANN generic and country-code suffixes that the
/// registry actually encounters plus the handful of private suffixes (<c>github.io</c> and friends)
/// whose omission would merge unrelated organisations. It implements the three PSL rule kinds:
/// ordinary, wildcard (<c>*.sch.uk</c>) and exception (<c>!city.kawasaki.jp</c>), and the standard
/// "longest matching rule wins, exception beats wildcard" resolution.
/// </summary>
public static class PublicSuffixList
{
    // Ordinary rules: an exact suffix. A domain whose trailing labels equal one of these has that as a
    // public suffix. Stored without the leading dot, lowercased.
    private static readonly FrozenSet<string> Rules = BuildRules();

    // Wildcard rules "*.x": any single label in front of x is a public suffix (so a.x is a suffix,
    // registrable domain is b.a.x). Stored as the parent "x".
    private static readonly FrozenSet<string> WildcardParents = new[]
    {
        "sch.uk", "ck", "kawasaki.jp", "kitakyushu.jp", "kobe.jp", "nagoya.jp",
        "sapporo.jp", "sendai.jp", "yokohama.jp",
    }.ToFrozenSet(StringComparer.Ordinal);

    // Exception rules "!x.y.z": x.y.z is NOT a public suffix; its parent y.z is. Stored as "x.y.z".
    private static readonly FrozenSet<string> Exceptions = new[]
    {
        "www.ck", "city.kawasaki.jp", "city.kitakyushu.jp", "city.kobe.jp", "city.nagoya.jp",
        "city.sapporo.jp", "city.sendai.jp", "city.yokohama.jp",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Returns the registrable domain (public suffix plus one label) for a lowercased, ASCII host, or
    /// <c>null</c> when the host is itself only a public suffix (no registrable part) or is empty.
    /// </summary>
    public static string? GetRegistrableDomain(string host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        var labels = host.Split('.');
        if (labels.Any(string.IsNullOrEmpty))
        {
            return null;
        }

        var suffixLabelCount = PublicSuffixLabelCount(labels);

        // A registrable domain needs at least one label in front of the public suffix.
        if (labels.Length <= suffixLabelCount)
        {
            return null;
        }

        var take = suffixLabelCount + 1;
        return string.Join('.', labels[^take..]);
    }

    // The number of trailing labels that form the public suffix, per the PSL algorithm.
    private static int PublicSuffixLabelCount(string[] labels)
    {
        // Exception rules take priority: if the host ends with an exception rule, the public suffix is
        // the rule minus its leftmost label.
        for (var start = 0; start < labels.Length; start++)
        {
            var candidate = string.Join('.', labels[start..]);
            if (Exceptions.Contains(candidate))
            {
                return labels.Length - start - 1;
            }
        }

        // Wildcard rules: "*.parent" makes (anything).parent a suffix, i.e. parent's label count + 1.
        for (var start = 0; start < labels.Length - 1; start++)
        {
            var parent = string.Join('.', labels[(start + 1)..]);
            if (WildcardParents.Contains(parent))
            {
                return labels.Length - start;
            }
        }

        // Ordinary rules: the longest trailing run of labels that is a known suffix.
        for (var start = 0; start < labels.Length; start++)
        {
            var candidate = string.Join('.', labels[start..]);
            if (Rules.Contains(candidate))
            {
                return labels.Length - start;
            }
        }

        // Unlisted TLD: the PSL default rule "*" means the rightmost label is the public suffix.
        return 1;
    }

    private static FrozenSet<string> BuildRules()
    {
        // Curated ICANN + private suffixes. Generic TLDs are single-label (the default "*" rule already
        // covers unlisted ones); the multi-label entries are the ccTLD second-levels and the private
        // suffixes that would otherwise merge unrelated organisations.
        var rules = new[]
        {
            // Common generic TLDs (single-label; explicit so the intent is greppable).
            "com", "org", "net", "io", "co", "dev", "app", "ai", "tech", "xyz", "info", "biz",
            "me", "us", "eu", "de", "fr", "es", "it", "nl", "se", "no", "fi", "dk", "pl", "cz",
            "ca", "ie", "ch", "at", "be", "pt", "ro", "ua", "in", "sg", "nz",

            // United Kingdom.
            "uk", "ac.uk", "co.uk", "gov.uk", "ltd.uk", "me.uk", "net.uk", "nhs.uk", "org.uk",
            "plc.uk", "police.uk",

            // Australia.
            "au", "asn.au", "com.au", "edu.au", "gov.au", "id.au", "net.au", "org.au",

            // Japan (organizational second-levels; the *.city.jp wildcards are handled separately).
            "jp", "ac.jp", "ad.jp", "co.jp", "ed.jp", "go.jp", "gr.jp", "lg.jp", "ne.jp", "or.jp",

            // A few more heavily-used ccTLD second levels.
            "com.br", "com.mx", "co.in", "co.nz", "co.za", "com.sg", "com.tr", "co.il",

            // Private suffixes: hosting/platform domains that are public-suffix-like — omitting them
            // would attribute two tenants' pages to one company.
            "github.io", "githubusercontent.com", "herokuapp.com", "herokussl.com",
            "s3.amazonaws.com", "blogspot.com", "netlify.app", "vercel.app", "pages.dev",
            "web.app", "firebaseapp.com", "azurewebsites.net", "cloudfront.net",
        };

        return rules.ToFrozenSet(StringComparer.Ordinal);
    }
}
