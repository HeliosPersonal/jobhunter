namespace JobHunter.Application.Commands;

/// <summary>
/// The resolution of a confirmation tap (SAD §6.3): the <see cref="Outcome"/> the Telegram layer acts on
/// and — for a <see cref="ConfirmationOutcome.Confirmed"/> — the <see cref="Command"/> and
/// <see cref="ArgumentTail"/> the tap runs. Only <see cref="ConfirmationOutcome.Confirmed"/> carries a
/// command; every refusal carries none, so there is no value shaped like a runnable command on a path
/// that must not run one.
/// </summary>
public sealed record ConfirmationResult
{
    private ConfirmationResult(ConfirmationOutcome outcome, string? command, string? argumentTail)
    {
        Outcome = outcome;
        Command = command;
        ArgumentTail = argumentTail;
    }

    public ConfirmationOutcome Outcome { get; }

    /// <summary>The command to run; non-null only when <see cref="Outcome"/> is <see cref="ConfirmationOutcome.Confirmed"/>.</summary>
    public string? Command { get; }

    /// <summary>The argument tail the confirmed command runs with; non-null only when confirmed.</summary>
    public string? ArgumentTail { get; }

    internal static ConfirmationResult Confirmed(string command, string argumentTail) =>
        new(ConfirmationOutcome.Confirmed, command, argumentTail);

    internal static ConfirmationResult Refused(ConfirmationOutcome outcome) =>
        new(outcome, command: null, argumentTail: null);
}
