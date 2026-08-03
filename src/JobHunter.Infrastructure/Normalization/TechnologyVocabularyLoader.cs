using System.Reflection;
using JobHunter.Application.Normalization;
using YamlDotNet.RepresentationModel;

namespace JobHunter.Infrastructure.Normalization;

/// <summary>
/// Reads the committed technology vocabulary (<c>technology-vocabulary.yaml</c>, T07) into the pure
/// Application-layer <see cref="TechnologyVocabulary"/>. The file ships as an embedded resource so the
/// running image needs no side-car file and the tagger cannot start with a missing vocabulary. It is a
/// boundary adapter — it lives in Infrastructure because it depends on YamlDotNet — and a static loader, so
/// it is not the leaked-service surface architecture rule 8 forbids. Parse failures throw: a malformed
/// vocabulary is an operator error caught at startup, not a business outcome the pipeline recovers from.
/// </summary>
public static class TechnologyVocabularyLoader
{
    private const string ResourceName =
        "JobHunter.Infrastructure.Normalization.technology-vocabulary.yaml";

    /// <summary>Loads and validates the embedded vocabulary, or throws naming what is wrong.</summary>
    public static TechnologyVocabulary Load()
    {
        var assembly = typeof(TechnologyVocabularyLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new TechnologyVocabularyException(
                $"The embedded technology vocabulary '{ResourceName}' is missing from the image.");

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses <paramref name="yaml"/> into a validated vocabulary, or throws naming the line.</summary>
    public static TechnologyVocabulary Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new TechnologyVocabularyException(
                $"The technology vocabulary is not valid YAML at line {ex.Start.Line}: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
        {
            return new TechnologyVocabulary([]);
        }

        if (stream.Documents[0].RootNode is not YamlSequenceNode root)
        {
            var line = stream.Documents[0].RootNode.Start.Line;
            throw new TechnologyVocabularyException(
                $"The technology vocabulary must be a YAML sequence of entries (line {line}).");
        }

        var entries = new List<TechnologyEntry>(root.Children.Count);
        foreach (var node in root.Children)
        {
            if (node is not YamlMappingNode mapping)
            {
                throw new TechnologyVocabularyException(
                    $"Technology vocabulary entry at line {node.Start.Line} is not a mapping.");
            }

            entries.Add(ReadEntry(mapping));
        }

        try
        {
            return new TechnologyVocabulary(entries);
        }
        catch (ArgumentException ex)
        {
            // The vocabulary is ambiguous (a blank/duplicate canonical, or a term claimed twice). That is a
            // load-time operator error surfaced by message, wrapped so the CLI shows one boundary type.
            throw new TechnologyVocabularyException(ex.Message, ex);
        }
    }

    private static TechnologyEntry ReadEntry(YamlMappingNode mapping)
    {
        long line = mapping.Start.Line;

        if (!mapping.Children.TryGetValue(new YamlScalarNode("canonical"), out var canonicalNode)
            || canonicalNode is not YamlScalarNode { Value: { } canonical }
            || string.IsNullOrWhiteSpace(canonical))
        {
            throw new TechnologyVocabularyException(
                $"Technology vocabulary entry at line {line} is missing a canonical name.");
        }

        var aliases = new List<string>();
        if (mapping.Children.TryGetValue(new YamlScalarNode("aliases"), out var aliasesNode))
        {
            if (aliasesNode is not YamlSequenceNode aliasSequence)
            {
                throw new TechnologyVocabularyException(
                    $"Technology '{canonical}' (line {line}) has an 'aliases' that is not a list.");
            }

            foreach (var alias in aliasSequence.Children)
            {
                if (alias is YamlScalarNode { Value: { } value } && !string.IsNullOrWhiteSpace(value))
                {
                    aliases.Add(value);
                }
            }
        }

        return new TechnologyEntry(canonical.Trim(), aliases);
    }
}

/// <summary>
/// A technology-vocabulary loading or validation failure. Thrown, not returned as a value: a malformed
/// vocabulary is an operator error caught at startup, not a business outcome the pipeline recovers from.
/// </summary>
public sealed class TechnologyVocabularyException : Exception
{
    public TechnologyVocabularyException(string message) : base(message)
    {
    }

    public TechnologyVocabularyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
