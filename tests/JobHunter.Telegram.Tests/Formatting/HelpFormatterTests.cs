using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// The <see cref="HelpFormatter"/> (AC-09): it renders the group structure and per-command usage that
/// <see cref="HelpText"/> derives from the registry into MarkdownV2, so the grouped <c>/help</c>, the
/// <c>/start</c> list and <c>/help [command]</c> present the surface without drift. Every dynamic value goes
/// through the one <see cref="MarkdownV2Escaper"/>, so a summary or example containing markup cannot break
/// the send.
/// </summary>
public sealed class HelpFormatterTests
{
    private static IReadOnlyList<CommandDescriptor> Sample =>
    [
        new("digest", "Re-read today's digest.", [],
            CommandCapability.Standard, CommandGroup.DigestAndDiscovery, false, "/digest"),
        new("more", "The next cards below the cut.",
            [new ArgumentSpec("count", required: false, "How many cards to show.")],
            CommandCapability.Standard, CommandGroup.DigestAndDiscovery, false, "/more", example: "/more 10"),
        new("run", "Trigger the daily pipeline.", [],
            CommandCapability.Sensitive, CommandGroup.Operations, true, "/run", "Start it?"),
    ];

    [Fact]
    public void Grouped_list_bolds_each_section_title()
    {
        var text = HelpFormatter.GroupedList(HelpText.Grouped(Sample));

        // Section titles are bold; the hyphen in the summary is escaped so the whole send does not fail.
        text.ShouldContain("*Digest and discovery*");
        text.ShouldContain("*Operations*");
    }

    [Fact]
    public void Grouped_list_lists_each_command_with_its_slash_and_summary()
    {
        var text = HelpFormatter.GroupedList(HelpText.Grouped(Sample));

        text.ShouldContain("/digest");
        text.ShouldContain("Re\\-read today's digest\\.");
        text.ShouldContain("/more");
        text.ShouldContain("/run");
    }

    [Fact]
    public void Grouped_list_orders_sections_and_commands_as_grouped()
    {
        var text = HelpFormatter.GroupedList(HelpText.Grouped(Sample));

        text.IndexOf("Digest and discovery", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("Operations", StringComparison.Ordinal));
        text.IndexOf("/digest", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("/more", StringComparison.Ordinal));
    }

    [Fact]
    public void Grouped_list_escapes_a_summary_that_contains_markup()
    {
        var hostile = new List<CommandDescriptor>
        {
            new("x", "Breaks (everything) with *markup*.", [],
                CommandCapability.Standard, CommandGroup.Meta, false, "/x"),
        };

        var text = HelpFormatter.GroupedList(HelpText.Grouped(hostile));

        text.ShouldNotContain("(everything)");
        text.ShouldContain("\\(everything\\)");
        text.ShouldContain("\\*markup\\*");
    }

    [Fact]
    public void Usage_shows_the_bold_signature_summary_and_example()
    {
        var more = Sample.Single(d => d.Name == "more");

        var text = HelpFormatter.Usage(HelpText.Usage(more));

        text.ShouldContain("*/more \\[count\\]*");
        text.ShouldContain("The next cards below the cut\\.");
        text.ShouldContain("count");
        text.ShouldContain("How many cards to show\\.");
        text.ShouldContain("/more 10");
    }

    [Fact]
    public void Usage_omits_the_example_line_when_there_is_none()
    {
        var digest = Sample.Single(d => d.Name == "digest");

        var text = HelpFormatter.Usage(HelpText.Usage(digest));

        text.ShouldContain("*/digest*");
        text.ShouldNotContain("Example");
    }

    [Fact]
    public void Usage_states_no_arguments_when_the_command_takes_none()
    {
        var digest = Sample.Single(d => d.Name == "digest");

        var text = HelpFormatter.Usage(HelpText.Usage(digest));

        text.ShouldContain("No arguments", Case.Insensitive);
    }

    [Fact]
    public void Grouped_list_rejects_null() =>
        Should.Throw<ArgumentNullException>(() => HelpFormatter.GroupedList(null!));

    [Fact]
    public void Usage_rejects_null() =>
        Should.Throw<ArgumentNullException>(() => HelpFormatter.Usage(null!));
}
