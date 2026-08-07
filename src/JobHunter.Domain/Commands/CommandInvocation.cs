using JobHunter.Domain.Common;

namespace JobHunter.Domain.Commands;

/// <summary>
/// One recorded run of one command — the atomic row of the append-only audit and usage log (F10
/// data-model §command_invocations). Its purpose is the metric in [[PRD]] §7 (which parts of the system
/// the Owner actually reaches for) and diagnosing a command that misbehaves; it feeds the p95 NFR through
/// <see cref="DurationMs"/>.
///
/// <para>The construction guards are the point of the type. It carries the <see cref="Command"/> name
/// (registry name, no slash), the <see cref="Outcome"/>, how long it took and <em>how many</em> arguments
/// it was given — never the arguments themselves. A <c>/search</c> query or a <c>/note</c> body can hold
/// anything the Owner typed; the count is enough for usage analysis and the text is not ours to keep
/// (data-model §command_invocations, SAD §8). There is no field that could hold argument content, so the
/// rule is a property of the type rather than a discipline at the call site.</para>
/// </summary>
public sealed class CommandInvocation : Entity
{
    public CommandInvocation(
        Guid id,
        long chatId,
        string command,
        CommandOutcome outcome,
        int durationMs,
        int argCount,
        DateTimeOffset invokedAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (!Enum.IsDefined(outcome) || outcome == CommandOutcome.Unspecified)
        {
            throw new ArgumentException(
                $"A command invocation must state its outcome; '{outcome}' is not a valid one.",
                nameof(outcome));
        }

        if (durationMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMs), durationMs, "Duration cannot be negative.");
        }

        if (argCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(argCount), argCount, "Argument count cannot be negative.");
        }

        ChatId = chatId;
        Command = command;
        Outcome = outcome;
        DurationMs = durationMs;
        ArgCount = argCount;
        InvokedAt = invokedAt;
    }

    private CommandInvocation()
    {
    }

    /// <summary>The Telegram chat the command came from.</summary>
    public long ChatId { get; private set; }

    /// <summary>The registry command name, no slash, e.g. <c>pipeline</c>.</summary>
    public string Command { get; private set; } = null!;

    public CommandOutcome Outcome { get; private set; }

    /// <summary>Wall-clock milliseconds from dispatch to outcome; feeds the p95 NFR.</summary>
    public int DurationMs { get; private set; }

    /// <summary>How many arguments the command was given — a count only, never their content.</summary>
    public int ArgCount { get; private set; }

    public DateTimeOffset InvokedAt { get; private set; }
}
