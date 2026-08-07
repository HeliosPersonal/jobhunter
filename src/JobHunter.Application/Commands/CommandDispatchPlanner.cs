using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// The pure heart of dispatch (SAD §6.1). Given a command word and its raw argument tail — an already
/// allowlisted, already conversation-checked message — it resolves against the <see cref="CommandRegistry"/>,
/// parses the arguments against the command's own inline-filter vocabulary, and reaches the one decision the
/// Telegram layer acts on: proceed, ask for a missing argument, report a malformed value, or confirm a
/// state-changing command. It holds no chat, no clock and no I/O, so the ordering that matters is testable
/// in isolation: an unknown word is never parsed, and a <see cref="CommandDescriptor.ChangesState"/> command
/// never returns <see cref="DispatchAction.Proceed"/> — there is no plan that reaches a handler without
/// confirmation (done-when #2).
///
/// <para>Allowlisting (done-when #1), the rate limit (done-when #3) and the invocation audit (done-when #4)
/// are orchestration the Telegram dispatcher wraps around this decision; they are deliberately not here,
/// because they need the chat, the clock and the store, and the decision does not.</para>
/// </summary>
public sealed class CommandDispatchPlanner
{
    private readonly CommandRegistry _registry;
    private readonly Func<CommandDescriptor, InlineFilterVocabulary> _vocabularyFor;

    public CommandDispatchPlanner(
        CommandRegistry registry,
        Func<CommandDescriptor, InlineFilterVocabulary> vocabularyFor)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _vocabularyFor = vocabularyFor ?? throw new ArgumentNullException(nameof(vocabularyFor));
    }

    /// <summary>Plans the dispatch of <paramref name="commandWord"/> (no slash) with its raw <paramref name="arguments"/> tail.</summary>
    public CommandDispatchPlan Plan(string commandWord, string? arguments)
    {
        ArgumentNullException.ThrowIfNull(commandWord);

        var command = _registry.Find(commandWord);
        if (command is null)
        {
            // An unknown word is never parsed — probing reveals nothing about the surface (SAD §6.1).
            return CommandDispatchPlan.Unknown();
        }

        var parsed = ArgumentParser.Parse(arguments, command, _vocabularyFor(command));

        return parsed.Status switch
        {
            ParseStatus.NeedsInput => CommandDispatchPlan.For(DispatchAction.NeedsInput, command, parsed),
            ParseStatus.Malformed => CommandDispatchPlan.For(DispatchAction.Malformed, command, parsed),
            // A state-changing command is confirmed before any handler runs; nothing else can proceed past here.
            _ when command.ChangesState => CommandDispatchPlan.For(DispatchAction.NeedsConfirmation, command, parsed),
            _ => CommandDispatchPlan.For(DispatchAction.Proceed, command, parsed),
        };
    }
}
