using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

/// <summary>
/// The client menu generation (AC-01, conformance assertion 3: registry → menu). The menu is projected from
/// the same descriptor list the router dispatches on, so <c>setMyCommands</c> carries exactly the registered
/// commands, with their summaries, and nothing else — the menu cannot drift from the surface.
/// </summary>
public sealed class BotMenuTests
{
    [Fact]
    public void Projects_each_descriptor_to_its_slashless_name_and_summary()
    {
        var descriptors = new List<CommandDescriptor>
        {
            new("digest", "Re-read today's digest", [], CommandCapability.Standard, CommandGroup.DigestAndDiscovery, false, "/digest"),
            new("run", "Trigger the daily pipeline", [], CommandCapability.Sensitive, CommandGroup.Operations, true, "/run", "Start it?"),
        };

        var menu = BotMenu.From(descriptors);

        menu.Select(m => (m.Command, m.Description)).ShouldBe(
        [
            ("digest", "Re-read today's digest"),
            ("run", "Trigger the daily pipeline"),
        ]);
    }

    [Fact]
    public void Preserves_the_catalogue_order() =>
        BotMenu.From(CommandCatalogue.Descriptors).Select(m => m.Command)
            .ShouldBe(CommandCatalogue.Descriptors.Select(d => d.Name));

    [Fact]
    public void The_generated_menu_contains_every_registered_command_and_nothing_else()
    {
        // Conformance assertion 3: the setMyCommands payload is exactly the registered set — Sensitive
        // commands included, because there is one Owner and hiding them would only make recovery harder.
        var registry = new CommandRegistry(CommandCatalogue.Descriptors);

        var menu = BotMenu.From(registry.Commands);

        menu.Select(m => m.Command).ShouldBe(registry.Commands.Select(c => c.Name));
        menu.Select(m => m.Description).ShouldBe(registry.Commands.Select(c => c.Summary));
    }

    [Fact]
    public void The_menu_names_carry_no_slash_because_setMyCommands_adds_it() =>
        BotMenu.From(CommandCatalogue.Descriptors).ShouldAllBe(m => !m.Command.StartsWith('/'));

    [Fact]
    public void Rejects_a_null_descriptor_list() =>
        Should.Throw<ArgumentNullException>(() => BotMenu.From(null!));
}
