using JobHunter.Application.Commands;
using JobHunter.Domain.Commands;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Commands;

/// <summary>
/// F10 T03 (SAD §6.1): the pure dispatch decision — resolve against the registry, parse the arguments,
/// then read the capability and state-changing flag. The rate limit, the invocation audit and the send
/// are orchestration around this decision (a later commit); the ordering that matters — an unknown command
/// is never parsed, a state-changing command never reaches a handler without confirmation — lives here and
/// is unit-tested with no chat and no clock.
/// </summary>
public sealed class CommandDispatchPlannerTests
{
    private static readonly InlineFilterVocabulary Search = new(
    [
        new InlineFilterSpec("tech", InlineFilterKind.Text),
        new InlineFilterSpec("min", InlineFilterKind.Number),
    ]);

    private static CommandDescriptor Pipeline() =>
        new("pipeline", "Your application pipeline.", [], CommandCapability.Standard, CommandGroup.Meta, false, "/pipeline");

    private static CommandDescriptor SearchCommand() =>
        new("search", "Search live jobs.",
            [new ArgumentSpec("query", required: false, "What to search for.")],
            CommandCapability.Standard, CommandGroup.Meta, false, "/search");

    private static CommandDescriptor Run() =>
        new("run", "Start a Run now.", [], CommandCapability.Sensitive, CommandGroup.Operations, changesState: true, "/run",
            confirmationPrompt: "Start a Run now?");

    private static CommandDescriptor Note() =>
        new("note", "Add a note.",
            [new ArgumentSpec("text", required: true, "The note text.")],
            CommandCapability.Standard, CommandGroup.Meta, false, "/note");

    private static CommandDispatchPlanner PlannerFor(params CommandDescriptor[] commands) =>
        new(new CommandRegistry(commands), _ => InlineFilterVocabulary.None);

    [Fact]
    public void Resolves_a_known_read_command_to_proceed()
    {
        var plan = PlannerFor(Pipeline()).Plan("pipeline", null);

        plan.Action.ShouldBe(DispatchAction.Proceed);
        plan.Command!.Name.ShouldBe("pipeline");
    }

    [Fact]
    public void Reports_an_unknown_command_without_parsing_anything()
    {
        var plan = PlannerFor(Pipeline()).Plan("teleport", "kafka");

        plan.Action.ShouldBe(DispatchAction.Unknown);
        plan.Command.ShouldBeNull();
        plan.Parsed.ShouldBeNull();
    }

    [Fact]
    public void A_state_changing_command_needs_confirmation_and_does_not_proceed()
    {
        // Done-when #2: there is no path to a handler that bypasses confirmation — the plan itself withholds it.
        var plan = PlannerFor(Run()).Plan("run", null);

        plan.Action.ShouldBe(DispatchAction.NeedsConfirmation);
        plan.Command!.Name.ShouldBe("run");
    }

    [Fact]
    public void A_missing_required_argument_enters_the_multi_step_flow_rather_than_erroring()
    {
        var plan = PlannerFor(Note()).Plan("note", null);

        plan.Action.ShouldBe(DispatchAction.NeedsInput);
        plan.Parsed!.MissingArgument!.Name.ShouldBe("text");
    }

    [Fact]
    public void A_malformed_value_names_the_problem_and_shows_the_usage_line()
    {
        var planner = new CommandDispatchPlanner(
            new CommandRegistry([SearchCommand()]),
            _ => Search);

        var plan = planner.Plan("search", "min:abc");

        plan.Action.ShouldBe(DispatchAction.Malformed);
        plan.Parsed!.Problem.ShouldNotBeNull();
        plan.Parsed.Problem.ShouldContain("min");
        plan.Parsed.Usage.ShouldNotBeNull();
        plan.Parsed.Usage.ShouldStartWith("/search");
    }

    [Fact]
    public void Passes_the_commands_own_vocabulary_to_the_parser()
    {
        var planner = new CommandDispatchPlanner(
            new CommandRegistry([SearchCommand()]),
            _ => Search);

        var plan = planner.Plan("search", "tech:go remote");

        plan.Action.ShouldBe(DispatchAction.Proceed);
        plan.Parsed!.Filters.ShouldHaveSingleItem().Key.ShouldBe("tech");
        plan.Parsed.FreeText.ShouldBe("remote");
    }

    [Fact]
    public void Defaults_to_no_vocabulary_when_none_is_provided()
    {
        // With the default provider a colon in an ordinary term is plain text, never a mis-filter.
        var plan = PlannerFor(SearchCommand()).Plan("search", "platform:internal");

        plan.Action.ShouldBe(DispatchAction.Proceed);
        plan.Parsed!.Filters.ShouldBeEmpty();
        plan.Parsed.FreeText.ShouldBe("platform:internal");
    }

    [Fact]
    public void Rejects_a_null_registry() =>
        Should.Throw<ArgumentNullException>(() => new CommandDispatchPlanner(null!, _ => InlineFilterVocabulary.None));
}
