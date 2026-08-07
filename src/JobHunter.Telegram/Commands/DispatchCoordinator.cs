using JobHunter.Application.Commands;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// The orchestration around the pure <see cref="CommandDispatchPlanner"/> (SAD §6.1, T03). The chat has
/// already passed the allowlist gate at the <see cref="Transport.OwnerGatedUpdateProcessor"/> (AC-10), so
/// what remains — and what the four done-when clauses turn on — happens here, in this order: apply the
/// per-chat rate limit, plan the command (resolve, parse, read the capability), then act on the plan.
///
/// <para>The load-bearing rules are structural, not conventions at a call site. A word that does not
/// resolve is never parsed and never invoked. A <see cref="CommandDescriptor.ChangesState"/> command
/// never reaches the invoker — it returns a confirmation prompt (the single-use nonce is T05), so there is
/// no path to a handler that bypasses confirmation (done-when #2). Every <em>terminal</em> outcome —
/// succeeded, unknown, malformed, throttled — is recorded through <see cref="ICommandInvocationLog"/> with
/// the command, the outcome, the duration and the argument <em>count</em>, never the argument content
/// (done-when #4). A command that only asks for more input (a missing argument, T04) or a confirmation
/// (T05) is not terminal, so its audit belongs to the step that completes it.</para>
///
/// <para>Recording is best-effort: a failed audit is an operational fault, not a failed command, so it is
/// logged and swallowed rather than allowed to reach the Owner.</para>
/// </summary>
internal sealed class DispatchCoordinator
{
    /// <summary>Runs the resolved command and returns its rendered messages — the seam to the handler set.</summary>
    public delegate Task<IReadOnlyList<RenderedMessage>> CommandInvoker(
        long chatId, string commandName, string? arguments, CancellationToken cancellationToken);

    private readonly CommandRateLimiter _rateLimiter;
    private readonly CommandDispatchPlanner _planner;
    private readonly ICommandInvocationLog _auditLog;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly CommandInvoker _invoke;
    private readonly ILogger<DispatchCoordinator> _logger;

    public DispatchCoordinator(
        CommandRateLimiter rateLimiter,
        CommandDispatchPlanner planner,
        ICommandInvocationLog auditLog,
        IClock clock,
        IIdGenerator idGenerator,
        CommandInvoker invoke,
        ILogger<DispatchCoordinator> logger)
    {
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Dispatches one allowlisted <c>/</c>-message and returns the messages to send back.</summary>
    public async Task<IReadOnlyList<RenderedMessage>> DispatchAsync(
        long chatId, string messageText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageText);

        var (commandWord, arguments) = Split(messageText);
        var argCount = CountArguments(arguments);
        var startedAt = _clock.UtcNow;

        // The rate limit comes before resolution so that even an unknown command counts against the budget —
        // a chat cannot probe the catalogue faster than the budget allows.
        var verdict = _rateLimiter.Check(chatId);
        if (verdict != RateVerdict.Allowed)
        {
            await RecordAsync(chatId, commandWord ?? string.Empty, CommandOutcome.Throttled, startedAt, argCount, cancellationToken)
                .ConfigureAwait(false);

            // The first over-budget command earns one throttle message; every later one this window is silent.
            return verdict == RateVerdict.Throttled
                ? [RenderedMessage.PlainText(ThrottleReply())]
                : [];
        }

        var plan = _planner.Plan(commandWord ?? string.Empty, arguments);
        switch (plan.Action)
        {
            case DispatchAction.Unknown:
                await RecordAsync(chatId, commandWord ?? string.Empty, CommandOutcome.Unknown, startedAt, argCount, cancellationToken)
                    .ConfigureAwait(false);
                return [RenderedMessage.PlainText(UnknownReply(commandWord))];

            case DispatchAction.Malformed:
                await RecordAsync(chatId, plan.Command!.Name, CommandOutcome.Malformed, startedAt, argCount, cancellationToken)
                    .ConfigureAwait(false);
                return [RenderedMessage.PlainText(MalformedReply(plan.Parsed!))];

            case DispatchAction.NeedsInput:
                // The multi-step flow is T04; a command awaiting input has not run, so it is not audited here.
                return [RenderedMessage.PlainText(NeedsInputReply(plan.Parsed!))];

            case DispatchAction.NeedsConfirmation:
                // The nonce and the tap are T05; issuing the prompt is not a terminal outcome, so no audit here.
                return [RenderedMessage.PlainText(ConfirmationReply(plan.Command!))];

            default:
                var messages = await _invoke(chatId, plan.Command!.Name, arguments, cancellationToken).ConfigureAwait(false);
                await RecordAsync(chatId, plan.Command.Name, CommandOutcome.Succeeded, startedAt, argCount, cancellationToken)
                    .ConfigureAwait(false);
                return messages;
        }
    }

    private async Task RecordAsync(
        long chatId, string command, CommandOutcome outcome, DateTimeOffset startedAt, int argCount, CancellationToken cancellationToken)
    {
        // An empty command word (a bare "/" that matched nothing) still needs a non-blank audit name.
        var name = string.IsNullOrWhiteSpace(command) ? "?" : command;
        var durationMs = (int)Math.Max(0, (_clock.UtcNow - startedAt).TotalMilliseconds);

        try
        {
            var invocation = new CommandInvocation(
                _idGenerator.NewId(), chatId, name, outcome, durationMs, argCount, _clock.UtcNow);
            await _auditLog.RecordAsync(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed audit is an operational fault, not a failed command — log and carry on (ICommandInvocationLog).
            _logger.LogError(ex, "Failed to record the invocation of command {Command}.", name);
        }
    }

    private static (string? CommandWord, string? Arguments) Split(string messageText)
    {
        var trimmed = messageText.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/')
        {
            return (null, null);
        }

        var spaceIndex = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var rawToken = spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];
        var arguments = spaceIndex < 0 ? null : trimmed[(spaceIndex + 1)..].Trim();

        // Drop the leading slash and any "@BotName" suffix Telegram appends to a command in a group.
        var word = rawToken[1..];
        var atIndex = word.IndexOf('@', StringComparison.Ordinal);
        if (atIndex >= 0)
        {
            word = word[..atIndex];
        }

        return (word, string.IsNullOrEmpty(arguments) ? null : arguments);
    }

    // A count of whitespace-separated tokens — enough for the usage metric, and the only argument fact kept.
    private static int CountArguments(string? arguments) =>
        string.IsNullOrWhiteSpace(arguments)
            ? 0
            : arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string ThrottleReply() =>
        "_" + MarkdownV2Escaper.Escape("Too many commands — try again in a minute.") + "_";

    private static string UnknownReply(string? commandWord) =>
        "_" + MarkdownV2Escaper.Escape(
            commandWord is null ? "Unknown command." : $"Unknown command: /{commandWord}.") + "_";

    // A Malformed parse always carries both a problem and a usage line (ParsedArguments.Malformed).
    private static string MalformedReply(ParsedArguments parsed) =>
        MarkdownV2Escaper.Escape(parsed.Problem!) + "\n" + MarkdownV2Escaper.Escape(parsed.Usage!);

    // A NeedsInput parse always names the argument it is waiting for (ParsedArguments.NeedsInput).
    private static string NeedsInputReply(ParsedArguments parsed) =>
        MarkdownV2Escaper.Escape($"Reply with {parsed.MissingArgument!.Name}; /cancel to stop.");

    // A ChangesState command always carries a confirmation prompt (CommandRegistry enforces it at startup).
    private static string ConfirmationReply(CommandDescriptor command) =>
        MarkdownV2Escaper.Escape(command.ConfirmationPrompt!);
}
