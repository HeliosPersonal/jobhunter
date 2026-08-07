using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

public sealed class CommandRegistryTests
{
    private static CommandDescriptor Read(string name) =>
        new(name, $"The {name} command.", [], CommandCapability.Standard, changesState: false, $"/{name}");

    private static CommandDescriptor StateChanging(string name, string? confirmationPrompt) =>
        new(name, $"The {name} command.", [], CommandCapability.Sensitive, changesState: true, $"/{name}",
            confirmationPrompt);

    [Fact]
    public void Exposes_the_descriptors_it_was_built_from()
    {
        var registry = new CommandRegistry([Read("digest"), Read("saved")]);

        registry.Commands.Select(c => c.Name).ShouldBe(["digest", "saved"]);
    }

    [Fact]
    public void Resolves_a_command_by_name()
    {
        var digest = Read("digest");
        var registry = new CommandRegistry([digest, Read("saved")]);

        registry.Find("digest").ShouldBe(digest);
    }

    [Fact]
    public void Returns_null_for_an_unknown_command() =>
        new CommandRegistry([Read("digest")]).Find("nope").ShouldBeNull();

    [Fact]
    public void Rejects_a_null_descriptor_list() =>
        Should.Throw<ArgumentNullException>(() => new CommandRegistry(null!));

    [Fact]
    public void Rejects_an_empty_surface() =>
        Should.Throw<ArgumentException>(() => new CommandRegistry([]));

    [Fact]
    public void Rejects_a_duplicate_command_name()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            new CommandRegistry([Read("digest"), Read("digest")]));

        ex.Message.ShouldContain("digest");
    }

    [Fact]
    public void Accepts_a_state_changing_command_that_declares_a_confirmation_prompt()
    {
        var registry = new CommandRegistry([StateChanging("run", "Start a Run now? Estimated cost $1.03.")]);

        registry.Find("run")!.ChangesState.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_a_state_changing_command_without_a_confirmation_prompt()
    {
        // QG-2: a command that mutates state but declares no confirmation path fails fast at
        // construction (startup), naming the offending command, so a malformed surface never serves traffic.
        var ex = Should.Throw<InvalidOperationException>(() =>
            new CommandRegistry([StateChanging("run", confirmationPrompt: null)]));

        ex.Message.ShouldContain("run");
    }

    [Fact]
    public void Rejects_a_state_changing_command_with_a_blank_confirmation_prompt()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            new CommandRegistry([StateChanging("run", confirmationPrompt: "   ")]));

        ex.Message.ShouldContain("run");
    }

    [Fact]
    public void Allows_a_read_command_to_omit_a_confirmation_prompt() =>
        new CommandRegistry([Read("digest")]).Find("digest").ShouldNotBeNull();
}
