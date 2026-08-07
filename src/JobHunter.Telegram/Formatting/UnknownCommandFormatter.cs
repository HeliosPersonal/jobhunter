using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// Renders the unknown-command reply (AC-09, ADR-F10-0002, contract §Unknown commands). It asks
/// <see cref="CommandSuggester"/> for the nearest command by Damerau–Levenshtein distance: within two edits
/// it names that command and points at <c>/help</c>, otherwise it serves the grouped list rather than a
/// guess. Never an LLM. The mistyped token is shown inside a code span so Telegram does not linkify the typo
/// itself, while the suggestion is a plain tappable <c>/command</c>; every dynamic value passes through the
/// one <see cref="MarkdownV2Escaper"/>, so a hostile token can never break the send.
/// </summary>
internal static class UnknownCommandFormatter
{
    public static string Reply(IReadOnlyList<CommandDescriptor> commands, string token)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(token);

        var suggestion = CommandSuggester.Nearest(commands, token);
        return suggestion is null
            ? GroupedFallback(commands)
            : Suggest(token, suggestion);
    }

    // A code span keeps the typo from being turned into a tappable command; the suggestion is the tappable one.
    private static string Suggest(string token, CommandDescriptor suggestion)
    {
        var typo = "`" + MarkdownV2Escaper.Escape(token.Trim()) + "`";
        var command = MarkdownV2Escaper.Escape("/" + suggestion.Name);
        var lead = MarkdownV2Escaper.Escape("Unknown command ") + typo + MarkdownV2Escaper.Escape(".");
        var line = MarkdownV2Escaper.Escape("Did you mean ") + command + MarkdownV2Escaper.Escape("?");
        var help = MarkdownV2Escaper.Escape("Or ") + MarkdownV2Escaper.Escape("/help")
            + MarkdownV2Escaper.Escape(" for everything.");

        return lead + "\n\n" + line + "\n" + help;
    }

    private static string GroupedFallback(IReadOnlyList<CommandDescriptor> commands)
    {
        var header = "_" + MarkdownV2Escaper.Escape("Unknown command.") + "_";
        return header + "\n\n" + HelpFormatter.GroupedList(HelpText.Grouped(commands));
    }
}
