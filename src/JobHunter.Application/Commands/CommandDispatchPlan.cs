using JobHunter.Domain.Commands;

namespace JobHunter.Application.Commands;

/// <summary>
/// The outcome of planning one command line (SAD §6.1): the <see cref="Action"/> the Telegram layer should
/// take, plus whatever that action needs. <see cref="Command"/> is null only for
/// <see cref="DispatchAction.Unknown"/> — an unknown word is never parsed, so <see cref="Parsed"/> is null
/// too. For every resolved command <see cref="Parsed"/> carries the typed arguments, and the action tells
/// the caller whether to run the handler, ask for a missing argument, report a malformed value or confirm.
/// </summary>
public sealed record CommandDispatchPlan
{
    private CommandDispatchPlan(DispatchAction action, CommandDescriptor? command, ParsedArguments? parsed)
    {
        Action = action;
        Command = command;
        Parsed = parsed;
    }

    public DispatchAction Action { get; }

    /// <summary>The resolved descriptor; null only when <see cref="Action"/> is <see cref="DispatchAction.Unknown"/>.</summary>
    public CommandDescriptor? Command { get; }

    /// <summary>The typed arguments; null only for an unknown command.</summary>
    public ParsedArguments? Parsed { get; }

    internal static CommandDispatchPlan Unknown() =>
        new(DispatchAction.Unknown, command: null, parsed: null);

    internal static CommandDispatchPlan For(
        DispatchAction action, CommandDescriptor command, ParsedArguments parsed) =>
        new(action, command, parsed);
}
