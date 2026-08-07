using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>One command's line in a grouped help section: its name and one-line summary.</summary>
public sealed record HelpLine(string Name, string Summary);

/// <summary>One section of the grouped help — a <see cref="CommandGroup"/>, its title and its lines, in order.</summary>
public sealed record HelpGroup(CommandGroup Group, string Title, IReadOnlyList<HelpLine> Lines);

/// <summary>The detailed usage of one command: its signature, summary, arguments and optional example.</summary>
public sealed record CommandUsage(
    string Command,
    string Signature,
    string Summary,
    IReadOnlyList<ArgumentSpec> Arguments,
    string? Example);

/// <summary>
/// The plain-text shape of the grouped <c>/help</c>, the <c>/start</c> list and the per-command
/// <c>/help [command]</c> usage (AC-09, contract §Meta) — derived from the same descriptor list the router
/// dispatches on, so the help a user reads is exactly the surface and cannot drift. This is the structure
/// only; MarkdownV2 escaping and Telegram framing live in the Telegram formatter, so the surface's shape is
/// unit-tested here without a rendering snapshot.
/// </summary>
public static class HelpText
{
    // The catalogue's section order and titles (command-catalogue.md ## headings), kept next to the enum
    // so the grouped help presents exactly the documented sections, in the documented order.
    private static readonly IReadOnlyList<(CommandGroup Group, string Title)> Sections =
    [
        (CommandGroup.DigestAndDiscovery, "Digest and discovery"),
        (CommandGroup.Pipeline, "Pipeline"),
        (CommandGroup.Company, "Company"),
        (CommandGroup.ProfileAndPreferences, "Profile and preferences"),
        (CommandGroup.Operations, "Operations"),
        (CommandGroup.Meta, "Meta"),
    ];

    /// <summary>
    /// The commands grouped into their catalogue sections, in section order and, within a section, in the
    /// descriptors' given order. A section with no commands is omitted rather than shown empty.
    /// </summary>
    public static IReadOnlyList<HelpGroup> Grouped(IReadOnlyList<CommandDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var groups = new List<HelpGroup>(Sections.Count);
        foreach (var (group, title) in Sections)
        {
            var lines = descriptors
                .Where(d => d.Group == group)
                .Select(d => new HelpLine(d.Name, d.Summary))
                .ToList();

            if (lines.Count > 0)
            {
                groups.Add(new HelpGroup(group, title, lines));
            }
        }

        return groups;
    }

    /// <summary>The detailed usage of one command: its slashed name, argument signature, summary and example.</summary>
    public static CommandUsage Usage(CommandDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new CommandUsage(
            Command: "/" + descriptor.Name,
            Signature: BuildSignature(descriptor),
            Summary: descriptor.Summary,
            Arguments: descriptor.Args,
            Example: descriptor.Example);
    }

    // Required arguments read as <name>, optional as [name] — the convention the catalogue headings use.
    private static string BuildSignature(CommandDescriptor descriptor)
    {
        var signature = "/" + descriptor.Name;
        foreach (var arg in descriptor.Args)
        {
            signature += arg.Required ? $" <{arg.Name}>" : $" [{arg.Name}]";
        }

        return signature;
    }
}
