using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

/// <summary>
/// The grouped help and per-command usage text (AC-09, contract §Meta), derived from the same descriptor
/// list the router dispatches on — the grouped <c>/help</c> and <c>/start</c> lists, and the detailed usage
/// line <c>/help [command]</c> serves. It is the plain-text shape only: MarkdownV2 escaping and Telegram
/// framing live in the Telegram formatter, so this stays a pure, snapshot-free unit.
/// </summary>
public sealed class HelpTextTests
{
    private static IReadOnlyList<CommandDescriptor> Sample =>
    [
        new("digest", "Re-read today's digest", [],
            CommandCapability.Standard, CommandGroup.DigestAndDiscovery, false, "/digest"),
        new("more", "The next cards below today's cut",
            [new ArgumentSpec("count", required: false, "How many cards to show.")],
            CommandCapability.Standard, CommandGroup.DigestAndDiscovery, false, "/more", example: "/more 10"),
        new("run", "Trigger the daily pipeline", [],
            CommandCapability.Sensitive, CommandGroup.Operations, true, "/run", "Start it?"),
    ];

    // ---- Grouped list (used by /help with no argument and by /start) ----

    [Fact]
    public void Grouped_lists_sections_in_group_order_with_a_heading_each()
    {
        var groups = HelpText.Grouped(Sample);

        groups.Select(g => g.Group).ShouldBe(
        [
            CommandGroup.DigestAndDiscovery,
            CommandGroup.Operations,
        ]);
    }

    [Fact]
    public void Grouped_places_each_command_in_its_declared_section_in_order()
    {
        var groups = HelpText.Grouped(Sample);

        var discovery = groups.Single(g => g.Group == CommandGroup.DigestAndDiscovery);
        discovery.Lines.Select(l => l.Name).ShouldBe(["digest", "more"]);
        discovery.Lines[0].Summary.ShouldBe("Re-read today's digest");
    }

    [Fact]
    public void Grouped_omits_a_section_that_has_no_commands()
    {
        var groups = HelpText.Grouped(Sample);

        groups.ShouldNotContain(g => g.Group == CommandGroup.Company);
    }

    [Fact]
    public void Grouped_uses_the_catalogue_section_titles()
    {
        var groups = HelpText.Grouped(Sample);

        groups.Single(g => g.Group == CommandGroup.DigestAndDiscovery).Title
            .ShouldBe("Digest and discovery");
        groups.Single(g => g.Group == CommandGroup.Operations).Title.ShouldBe("Operations");
    }

    [Fact]
    public void Grouped_rejects_a_null_descriptor_list() =>
        Should.Throw<ArgumentNullException>(() => HelpText.Grouped(null!));

    // ---- Per-command usage (/help [command]) ----

    [Fact]
    public void Usage_names_the_command_its_summary_arguments_and_example()
    {
        var more = Sample.Single(d => d.Name == "more");

        var usage = HelpText.Usage(more);

        usage.Command.ShouldBe("/more");
        usage.Summary.ShouldBe("The next cards below today's cut");
        usage.Arguments.ShouldHaveSingleItem();
        usage.Arguments[0].Name.ShouldBe("count");
        usage.Arguments[0].Required.ShouldBeFalse();
        usage.Arguments[0].Description.ShouldBe("How many cards to show.");
        usage.Example.ShouldBe("/more 10");
    }

    [Fact]
    public void Usage_renders_the_argument_signature_with_optional_in_brackets_and_required_in_angles()
    {
        var more = Sample.Single(d => d.Name == "more");

        HelpText.Usage(more).Signature.ShouldBe("/more [count]");
    }

    [Fact]
    public void Usage_signature_is_the_bare_command_when_it_takes_no_arguments()
    {
        var digest = Sample.Single(d => d.Name == "digest");

        HelpText.Usage(digest).Signature.ShouldBe("/digest");
    }

    [Fact]
    public void Usage_has_no_example_when_the_descriptor_declares_none()
    {
        var digest = Sample.Single(d => d.Name == "digest");

        HelpText.Usage(digest).Example.ShouldBeNull();
    }

    [Fact]
    public void Usage_rejects_a_null_descriptor() =>
        Should.Throw<ArgumentNullException>(() => HelpText.Usage(null!));
}
