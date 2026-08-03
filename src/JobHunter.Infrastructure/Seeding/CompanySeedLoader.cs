using JobHunter.Application.Discovery;
using JobHunter.Domain.Companies;
using YamlDotNet.RepresentationModel;

namespace JobHunter.Infrastructure.Seeding;

/// <summary>
/// Reads and schema-validates the curated company seed (<c>tools/seed/companies.yaml</c>, T03). The file
/// is a YAML sequence of mappings, one per company. Validation is strict and positional: a missing or
/// malformed field fails the load with a message naming the 1-based line, so a bad edit is fixed by
/// jumping to it rather than by bisecting a 300-entry file. It is a boundary adapter — it lives in
/// Infrastructure because it depends on YamlDotNet — and it produces the pure
/// <see cref="CompanySeedEntry"/> records the Application-layer <see cref="CompanyRegistryService"/> upserts.
/// </summary>
public static class CompanySeedLoader
{
    /// <summary>Parses <paramref name="yaml"/> into validated seed entries, or throws naming the offending line.</summary>
    public static IReadOnlyList<CompanySeedEntry> Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new CompanySeedException(
                $"The company seed is not valid YAML at line {ex.Start.Line}: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
        {
            return [];
        }

        if (stream.Documents[0].RootNode is not YamlSequenceNode root)
        {
            var line = stream.Documents[0].RootNode.Start.Line;
            throw new CompanySeedException(
                $"The company seed must be a YAML sequence of company entries (line {line}).");
        }

        var entries = new List<CompanySeedEntry>(root.Children.Count);
        var seenDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in root.Children)
        {
            if (node is not YamlMappingNode mapping)
            {
                throw new CompanySeedException($"Company seed entry at line {node.Start.Line} is not a mapping.");
            }

            entries.Add(ReadEntry(mapping, seenDomains));
        }

        return entries;
    }

    private static CompanySeedEntry ReadEntry(YamlMappingNode mapping, HashSet<string> seenDomains)
    {
        long line = mapping.Start.Line;

        var domain = Required(mapping, "domain", line);
        var displayName = Required(mapping, "display_name", line);
        var atsKindRaw = Required(mapping, "ats_kind", line);
        var boardToken = Required(mapping, "board_token", line);

        if (!Enum.TryParse<AtsKind>(atsKindRaw, ignoreCase: false, out var atsKind)
            || !Enum.IsDefined(atsKind))
        {
            throw new CompanySeedException(
                $"Company seed entry at line {line} has an unknown ats_kind '{atsKindRaw}'. " +
                $"Expected one of: {string.Join(", ", Enum.GetNames<AtsKind>())}.");
        }

        // Fail fast on a domain that will not canonicalise, so the seeder never has to reject a row.
        if (CanonicalDomain.TryCreate(domain).IsFailure)
        {
            throw new CompanySeedException(
                $"Company seed entry at line {line} has a non-canonicalisable domain '{domain}'.");
        }

        if (!seenDomains.Add(domain))
        {
            throw new CompanySeedException(
                $"Company seed entry at line {line} repeats domain '{domain}'; each domain must appear once.");
        }

        return new CompanySeedEntry(
            domain,
            displayName,
            atsKind,
            boardToken,
            Optional(mapping, "careers_url"),
            Optional(mapping, "hq_country"));
    }

    private static string Required(YamlMappingNode mapping, string key, long line)
    {
        var value = Optional(mapping, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CompanySeedException($"Company seed entry at line {line} is missing required field '{key}'.");
        }

        return value;
    }

    private static string? Optional(YamlMappingNode mapping, string key)
    {
        if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var node)
            && node is YamlScalarNode scalar
            && !string.IsNullOrWhiteSpace(scalar.Value))
        {
            return scalar.Value.Trim();
        }

        return null;
    }
}

/// <summary>
/// A curated-seed loading or validation failure. Thrown, not returned as a value: a malformed seed file
/// is an operator error caught at deploy time by the <c>seed</c> command, not a business outcome the
/// pipeline recovers from.
/// </summary>
public sealed class CompanySeedException : Exception
{
    public CompanySeedException(string message) : base(message)
    {
    }

    public CompanySeedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
