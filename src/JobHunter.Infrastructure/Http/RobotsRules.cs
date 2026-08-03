namespace JobHunter.Infrastructure.Http;

/// <summary>
/// A parsed <c>robots.txt</c> reduced to the allow/disallow rules that apply to our user-agent (AC-06).
/// Parsing is pure so it is unit-tested against recorded <c>robots.txt</c> bodies with zero network; the
/// caching and fetching live in <see cref="RobotsPolicy"/>. Precedence follows the de-facto standard:
/// the most specific (longest) matching rule wins, and <c>Allow</c> beats <c>Disallow</c> on a tie.
/// </summary>
internal sealed class RobotsRules
{
    private readonly List<Rule> _rules;

    private RobotsRules(List<Rule> rules) => _rules = rules;

    /// <summary>A permissive policy: everything is allowed. The reading for an unreachable file (AC-06).</summary>
    public static RobotsRules AllowAll { get; } = new([]);

    /// <summary>A conservative policy: everything is disallowed. The reading for a malformed file (AC-06).</summary>
    public static RobotsRules DenyAll { get; } = new([new Rule("/", false)]);

    /// <summary>
    /// Parses a <c>robots.txt</c> body, keeping the rules for the group that matches
    /// <paramref name="userAgent"/> — its specific product token if present, otherwise the <c>*</c> group.
    /// A body that contains no usable group is <see cref="AllowAll"/> (nothing forbade us).
    /// </summary>
    public static RobotsRules Parse(string body, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(userAgent);

        var token = ProductToken(userAgent);

        // Collect rules per group. A group is one or more User-agent lines followed by rule lines.
        var groups = new Dictionary<string, List<Rule>>(StringComparer.OrdinalIgnoreCase);
        var activeAgents = new List<string>();
        var sawRuleSinceAgent = false;

        foreach (var raw in body.Split('\n'))
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var field = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (field.Equals("User-agent", StringComparison.OrdinalIgnoreCase))
            {
                // A new agent line after some rules starts a fresh group.
                if (sawRuleSinceAgent)
                {
                    activeAgents.Clear();
                    sawRuleSinceAgent = false;
                }

                activeAgents.Add(value);
                if (!groups.ContainsKey(value))
                {
                    groups[value] = [];
                }
            }
            else if (field.Equals("Disallow", StringComparison.OrdinalIgnoreCase)
                     || field.Equals("Allow", StringComparison.OrdinalIgnoreCase))
            {
                sawRuleSinceAgent = true;
                var allow = field.Equals("Allow", StringComparison.OrdinalIgnoreCase);

                // An empty Disallow means "allow everything" for the group and carries no path rule.
                foreach (var agent in activeAgents)
                {
                    if (value.Length == 0 && !allow)
                    {
                        continue;
                    }

                    groups[agent].Add(new Rule(value, allow));
                }
            }
        }

        if (groups.TryGetValue(token, out var specific))
        {
            return new RobotsRules(specific);
        }

        if (groups.TryGetValue("*", out var wildcard))
        {
            return new RobotsRules(wildcard);
        }

        return AllowAll;
    }

    /// <summary>True when <paramref name="path"/> may be fetched under these rules (AC-06).</summary>
    public bool IsAllowed(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0)
        {
            path = "/";
        }

        Rule? best = null;
        foreach (var rule in _rules)
        {
            if (!path.StartsWith(rule.Path, StringComparison.Ordinal))
            {
                continue;
            }

            // Longest match wins; on equal length an Allow beats a Disallow (standard precedence).
            if (best is null
                || rule.Path.Length > best.Value.Path.Length
                || (rule.Path.Length == best.Value.Path.Length && rule.Allow))
            {
                best = rule;
            }
        }

        return best?.Allow ?? true;
    }

    private static string ProductToken(string userAgent)
    {
        // "JobHunter/1.0 (+https://…)" → "JobHunter". robots groups match the bare product token.
        var slash = userAgent.IndexOf('/', StringComparison.Ordinal);
        var space = userAgent.IndexOf(' ', StringComparison.Ordinal);
        var end = slash >= 0 ? slash : space >= 0 ? space : userAgent.Length;
        return userAgent[..end];
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 ? line[..hash] : line;
    }

    private readonly record struct Rule(string Path, bool Allow);
}
