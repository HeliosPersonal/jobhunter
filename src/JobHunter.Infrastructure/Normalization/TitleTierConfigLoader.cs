using JobHunter.Application.Normalization;
using YamlDotNet.RepresentationModel;

namespace JobHunter.Infrastructure.Normalization;

/// <summary>
/// Reads the committed title-tier reference config (<c>title-tiers.yaml</c>, T11) into the pure
/// Application-layer <see cref="TitleTierConfig"/>. The file ships as an embedded resource so the running
/// image needs no side-car file. It is a boundary adapter — it lives in Infrastructure because it depends
/// on YamlDotNet — and a static loader, so it is not the leaked-service surface architecture rule 8 forbids.
/// Parse failures throw: a malformed reference is an operator error caught at startup, not a business
/// outcome the pipeline recovers from.
/// </summary>
public static class TitleTierConfigLoader
{
    private const string ResourceName =
        "JobHunter.Infrastructure.Normalization.title-tiers.yaml";

    /// <summary>Loads and validates the embedded title-tier config, or throws naming what is wrong.</summary>
    public static TitleTierConfig Load()
    {
        var assembly = typeof(TitleTierConfigLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new TitleTierConfigException(
                $"The embedded title-tier config '{ResourceName}' is missing from the image.");

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses <paramref name="yaml"/> into a validated config, or throws naming the line.</summary>
    public static TitleTierConfig Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new TitleTierConfigException(
                $"The title-tier config is not valid YAML at line {ex.Start.Line}: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
        {
            return new TitleTierConfig([]);
        }

        if (stream.Documents[0].RootNode is not YamlSequenceNode root)
        {
            var line = stream.Documents[0].RootNode.Start.Line;
            throw new TitleTierConfigException(
                $"The title-tier config must be a YAML sequence of entries (line {line}).");
        }

        var entries = new List<TitleTierEntry>(root.Children.Count);
        foreach (var node in root.Children)
        {
            if (node is not YamlMappingNode mapping)
            {
                throw new TitleTierConfigException(
                    $"Title-tier entry at line {node.Start.Line} is not a mapping.");
            }

            entries.Add(ReadEntry(mapping));
        }

        try
        {
            return new TitleTierConfig(entries);
        }
        catch (ArgumentException ex)
        {
            // The config is ambiguous (a blank/duplicate title) or drifts from the F3 vocabulary (an unknown
            // role family). That is a load-time operator error surfaced by message, wrapped so the CLI shows
            // one boundary type.
            throw new TitleTierConfigException(ex.Message, ex);
        }
    }

    private static TitleTierEntry ReadEntry(YamlMappingNode mapping)
    {
        long line = mapping.Start.Line;

        var tierRaw = RequiredScalar(mapping, "tier", line);
        if (!Enum.TryParse<TitleTier>(tierRaw, ignoreCase: false, out var tier) || !Enum.IsDefined(tier))
        {
            throw new TitleTierConfigException(
                $"Title-tier entry at line {line} has an unknown tier '{tierRaw}'. " +
                $"Expected one of: {string.Join(", ", Enum.GetNames<TitleTier>())}.");
        }

        var title = RequiredScalar(mapping, "title", line);
        var roleFamily = RequiredScalar(mapping, "role_family", line);

        return new TitleTierEntry(tier, title, roleFamily);
    }

    private static string RequiredScalar(YamlMappingNode mapping, string key, long line)
    {
        if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var node)
            && node is YamlScalarNode scalar
            && !string.IsNullOrWhiteSpace(scalar.Value))
        {
            return scalar.Value.Trim();
        }

        throw new TitleTierConfigException(
            $"Title-tier entry at line {line} is missing required field '{key}'.");
    }
}

/// <summary>
/// A title-tier config loading or validation failure. Thrown, not returned as a value: a malformed
/// reference is an operator error caught at startup, not a business outcome the pipeline recovers from.
/// </summary>
public sealed class TitleTierConfigException : Exception
{
    public TitleTierConfigException(string message) : base(message)
    {
    }

    public TitleTierConfigException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
