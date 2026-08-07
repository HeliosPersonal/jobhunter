using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Commands;

public sealed class CommandDescriptorTests
{
    private static CommandDescriptor Valid(
        CommandCapability capability = CommandCapability.Standard,
        bool changesState = false,
        string? confirmationPrompt = null) =>
        new("run", "Starts a Run now.", [], capability, changesState, "/run", confirmationPrompt);

    [Fact]
    public void Exposes_its_declared_fields()
    {
        var arg = new ArgumentSpec("count", required: false, "How many cards.");

        var descriptor = new CommandDescriptor(
            "more", "The next cards below the cut.", [arg],
            CommandCapability.Standard, changesState: false, "/more");

        descriptor.Name.ShouldBe("more");
        descriptor.Summary.ShouldBe("The next cards below the cut.");
        descriptor.Args.ShouldHaveSingleItem().ShouldBe(arg);
        descriptor.Capability.ShouldBe(CommandCapability.Standard);
        descriptor.ChangesState.ShouldBeFalse();
        descriptor.ContractAnchor.ShouldBe("/more");
        descriptor.ConfirmationPrompt.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_name(string name) =>
        Should.Throw<ArgumentException>(() =>
            new CommandDescriptor(name, "s", [], CommandCapability.Standard, false, "/x"));

    [Fact]
    public void Rejects_a_null_name() =>
        Should.Throw<ArgumentException>(() =>
            new CommandDescriptor(null!, "s", [], CommandCapability.Standard, false, "/x"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_summary(string summary) =>
        Should.Throw<ArgumentException>(() =>
            new CommandDescriptor("x", summary, [], CommandCapability.Standard, false, "/x"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_contract_anchor(string anchor) =>
        Should.Throw<ArgumentException>(() =>
            new CommandDescriptor("x", "s", [], CommandCapability.Standard, false, anchor));

    [Fact]
    public void Rejects_a_null_arguments_list() =>
        Should.Throw<ArgumentNullException>(() =>
            new CommandDescriptor("x", "s", null!, CommandCapability.Standard, false, "/x"));

    [Fact]
    public void Cannot_be_constructed_without_a_capability()
    {
        // The guard is at construction, not at dispatch: a descriptor that forgot its capability (the
        // default enum value) fails closed rather than silently defaulting to an everyday command.
        Should.Throw<ArgumentException>(() => Valid(capability: CommandCapability.Unspecified));
    }

    [Fact]
    public void Rejects_an_undefined_capability() =>
        Should.Throw<ArgumentException>(() => Valid(capability: (CommandCapability)99));

    [Fact]
    public void Allows_a_state_changing_descriptor_to_omit_its_confirmation_prompt()
    {
        // Construction succeeds; the missing confirmation path is a registry-validation failure, not a
        // construction one — the two guards deliberately live at different layers (Done-when a vs b).
        var descriptor = Valid(changesState: true, confirmationPrompt: null);

        descriptor.ChangesState.ShouldBeTrue();
        descriptor.ConfirmationPrompt.ShouldBeNull();
    }

    [Fact]
    public void Carries_a_confirmation_prompt_when_given_one()
    {
        var descriptor = Valid(changesState: true, confirmationPrompt: "Start a Run now?");

        descriptor.ConfirmationPrompt.ShouldBe("Start a Run now?");
    }
}
