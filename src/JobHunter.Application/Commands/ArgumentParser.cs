using System.Globalization;
using System.Text;
using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// Turns a command's raw argument string into typed values (SAD §6.1, T02). The parser is deliberately
/// forgiving — the catalogue's §Argument-parsing table is its specification:
///
/// <list type="bullet">
///   <item>A missing required argument is <see cref="ParseStatus.NeedsInput"/>, the entry to the
///     multi-step flow, never an error reply.</item>
///   <item>An <c>key:value</c> token whose key is a declared filter becomes a typed
///     <see cref="ParsedFilter"/>; an unknown key is treated as free text, with a note.</item>
///   <item>A value that cannot fit its filter kind (<c>min:abc</c>) is <see cref="ParseStatus.Malformed"/>,
///     named explicitly with the usage line.</item>
///   <item>Quoted phrases survive as one term; recognised filters are deduplicated.</item>
/// </list>
///
/// <para>The point of the design (done-when #6): recognised filters are lifted out into typed pairs and
/// the free text that remains carries no filter syntax, so no user value ever reaches a query as a raw
/// concatenated blob (F9 SAD §8).</para>
/// </summary>
public static class ArgumentParser
{
    private static readonly HashSet<string> BooleanValues =
        new(StringComparer.Ordinal) { "yes", "no", "true", "false" };

    private static readonly char[] DurationUnits = ['d', 'w', 'm', 'y'];

    public static ParsedArguments Parse(string? arguments, CommandDescriptor descriptor, InlineFilterVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var freeTerms = new List<string>();
        var filters = new List<ParsedFilter>();
        var notes = new List<string>();
        var seenFilters = new HashSet<string>(StringComparer.Ordinal);
        var hasFilterVocabulary = VocabularyHasEntries(vocabulary);

        foreach (var (text, quoted) in Tokenize(arguments))
        {
            if (quoted)
            {
                freeTerms.Add(text);
                continue;
            }

            var colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon == text.Length - 1)
            {
                // No key, or a bare "key:" with no value — an ordinary term that happens to hold a colon.
                freeTerms.Add(text);
                continue;
            }

            var key = text[..colon];
            var rawValue = text[(colon + 1)..];
            var spec = vocabulary.Find(key);
            if (spec is null)
            {
                if (hasFilterVocabulary)
                {
                    notes.Add($"\"{text}\" is not a recognised filter, so I searched for it as text.");
                }

                freeTerms.Add(text);
                continue;
            }

            var value = rawValue.ToLowerInvariant();
            if (!IsValidValue(value, spec.Kind))
            {
                return ParsedArguments.Malformed(
                    $"{spec.Key}: \"{rawValue}\" is not a valid {DescribeKind(spec.Kind)}.",
                    Usage(descriptor));
            }

            if (seenFilters.Add($"{spec.Key}\0{value}"))
            {
                filters.Add(new ParsedFilter(spec.Key, value));
            }
        }

        var freeText = string.Join(' ', freeTerms);

        // A command that declares no positional arguments ignores whatever was typed, with a note
        // (catalogue §Argument parsing: "Extra arguments — ignored, with a note").
        if (descriptor.Args.Count == 0)
        {
            if (freeText.Length > 0 || filters.Count > 0)
            {
                notes.Add("This command takes no arguments; the extra input was ignored.");
            }

            return ParsedArguments.Complete(string.Empty, [], notes);
        }

        // A required argument with nothing to fill it opens the multi-step flow rather than erroring.
        if (freeText.Length == 0)
        {
            var required = descriptor.Args.FirstOrDefault(a => a.Required);
            if (required is not null)
            {
                return ParsedArguments.NeedsInput(required);
            }
        }

        return ParsedArguments.Complete(freeText, filters, notes);
    }

    // A vocabulary is "active" when it declares at least one filter; only then does an unrecognised
    // key:value earn a note, so a colon in an ordinary term stays silent for a filter-free command.
    private static bool VocabularyHasEntries(InlineFilterVocabulary vocabulary) =>
        !ReferenceEquals(vocabulary, InlineFilterVocabulary.None);

    private static bool IsValidValue(string value, InlineFilterKind kind) => kind switch
    {
        InlineFilterKind.Text => value.Length > 0,
        InlineFilterKind.Number =>
            double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        InlineFilterKind.Duration => IsDuration(value),
        InlineFilterKind.Boolean => BooleanValues.Contains(value),
        _ => false,
    };

    private static bool IsDuration(string value)
    {
        if (value.Length < 2 || !DurationUnits.Contains(value[^1]))
        {
            return false;
        }

        for (var i = 0; i < value.Length - 1; i++)
        {
            if (!char.IsAsciiDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeKind(InlineFilterKind kind) => kind switch
    {
        InlineFilterKind.Number => "number",
        InlineFilterKind.Duration => "duration (e.g. 30d)",
        InlineFilterKind.Boolean => "yes/no value",
        _ => "value",
    };

    private static string Usage(CommandDescriptor descriptor)
    {
        var builder = new StringBuilder($"/{descriptor.Name}");
        foreach (var arg in descriptor.Args)
        {
            builder.Append(arg.Required ? $" <{arg.Name}>" : $" [{arg.Name}]");
        }

        return builder.ToString();
    }

    // Split on whitespace but keep a double-quoted phrase as one term. A quoted term is never read as a
    // filter even if it contains a colon, so "staff: engineer" stays a single search phrase.
    private static IEnumerable<(string Text, bool Quoted)> Tokenize(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            yield break;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var quotedTerm = false;

        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                if (inQuotes)
                {
                    yield return (current.ToString(), true);
                    current.Clear();
                    inQuotes = false;
                    quotedTerm = false;
                }
                else
                {
                    if (current.Length > 0)
                    {
                        yield return (current.ToString(), false);
                        current.Clear();
                    }

                    inQuotes = true;
                    quotedTerm = true;
                }

                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return (current.ToString(), false);
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            yield return (current.ToString(), quotedTerm);
        }
    }
}
