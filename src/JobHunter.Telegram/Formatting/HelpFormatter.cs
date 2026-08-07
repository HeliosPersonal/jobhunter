using System.Text;
using JobHunter.Application.Commands;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// Renders the registry-derived help structure into MarkdownV2 (AC-09, contract §Meta): the grouped
/// <c>/help</c> and <c>/start</c> list, and the per-command <c>/help [command]</c> usage. The structure
/// comes from <see cref="HelpText"/>, so the surface a user reads is the same list the router dispatches on
/// and cannot drift; this type only frames it. Every dynamic value passes through the one
/// <see cref="MarkdownV2Escaper"/>, so a summary, argument description or example containing markup can
/// never break the send.
/// </summary>
internal static class HelpFormatter
{
    /// <summary>The grouped command list: a bold section title, then one line per command in the section.</summary>
    public static string GroupedList(IReadOnlyList<HelpGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var builder = new StringBuilder();
        foreach (var group in groups)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append('*').Append(MarkdownV2Escaper.Escape(group.Title)).Append("*\n");
            foreach (var line in group.Lines)
            {
                builder.Append(MarkdownV2Escaper.Escape("/" + line.Name))
                    .Append(" — ")
                    .Append(MarkdownV2Escaper.Escape(line.Summary))
                    .Append('\n');
            }
        }

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>One command's detailed usage: the bold signature, the summary, its arguments and an example.</summary>
    public static string Usage(CommandUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var builder = new StringBuilder();
        builder.Append('*').Append(MarkdownV2Escaper.Escape(usage.Signature)).Append("*\n");
        builder.Append(MarkdownV2Escaper.Escape(usage.Summary)).Append('\n');

        if (usage.Arguments.Count == 0)
        {
            builder.Append('\n').Append(MarkdownV2Escaper.Escape("No arguments."));
        }
        else
        {
            builder.Append('\n');
            foreach (var arg in usage.Arguments)
            {
                var required = arg.Required ? "required" : "optional";
                builder.Append(MarkdownV2Escaper.Escape($"• {arg.Name} ({required}) — {arg.Description}"))
                    .Append('\n');
            }

            builder.Length--; // drop the trailing newline the loop left
        }

        if (!string.IsNullOrWhiteSpace(usage.Example))
        {
            builder.Append('\n').Append(MarkdownV2Escaper.Escape("Example: " + usage.Example));
        }

        return builder.ToString();
    }
}
