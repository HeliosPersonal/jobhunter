using System.Collections.Frozen;
using System.Text;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// The command dispatcher (T11, contract §Commands, ADR-F10-0002). It reads the leading <c>/token</c> of a
/// message, looks up the registered <see cref="ICommandHandler"/> and hands it the remaining arguments and
/// the Owner's chat id. Anything it does not recognise — an unknown command, a bare word, an empty line —
/// gets a single "unknown command" reply followed by the help list, and nothing else: there is no
/// conversational fallback and no LLM anywhere in this path, because a bot that tries to chat sets an
/// expectation this product does not meet (ADR-F10-0002).
///
/// <para>The <c>/help</c> list is derived from the same registrations the router dispatches on, so a routable
/// command is always listed and a listed command is always routable — they cannot drift. Matching is
/// case-insensitive and tolerates the <c>@BotName</c> suffix Telegram appends to commands in groups.</para>
/// </summary>
internal sealed class CommandRouter
{
    private readonly FrozenDictionary<string, ICommandHandler> _handlers;
    private readonly string _helpList;
    private readonly ILogger<CommandRouter> _logger;

    public CommandRouter(IReadOnlyList<CommandRegistration> registrations, ILogger<CommandRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _handlers = registrations.ToFrozenDictionary(
            r => r.Token, r => r.Handler, StringComparer.OrdinalIgnoreCase);
        _helpList = BuildHelpList(registrations);
    }

    /// <summary>The rendered command list, so <c>/help</c> can serve exactly what the router dispatches on.</summary>
    public string HelpList => _helpList;

    public async Task<IReadOnlyList<RenderedMessage>> RouteAsync(
        long chatId, string messageText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageText);

        var (token, arguments) = Split(messageText);
        if (token is not null && _handlers.TryGetValue(token, out var handler))
        {
            return await handler.HandleAsync(new CommandRequest(chatId, arguments), cancellationToken)
                .ConfigureAwait(false);
        }

        // Unknown command: one line plus the help list, no fallback, no LLM (ADR-F10-0002).
        _logger.LogDebug("Unrecognised command; replied with the help list.");
        return [RenderedMessage.PlainText(UnknownCommandReply())];
    }

    private string UnknownCommandReply() =>
        "_" + MarkdownV2Escaper.Escape("Unknown command.") + "_\n\n" + _helpList;

    private static (string? Token, string? Arguments) Split(string messageText)
    {
        var trimmed = messageText.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/')
        {
            return (null, null);
        }

        var spaceIndex = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var rawToken = spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];
        var arguments = spaceIndex < 0 ? null : trimmed[(spaceIndex + 1)..].Trim();

        // Telegram appends @BotName to a command in a group chat; strip it so "/start@Bot" matches "/start".
        var atIndex = rawToken.IndexOf('@', StringComparison.Ordinal);
        var token = atIndex < 0 ? rawToken : rawToken[..atIndex];

        return (token, string.IsNullOrEmpty(arguments) ? null : arguments);
    }

    private static string BuildHelpList(IReadOnlyList<CommandRegistration> registrations)
    {
        var builder = new StringBuilder();
        foreach (var registration in registrations)
        {
            builder.Append(MarkdownV2Escaper.Escape(registration.Token))
                .Append(" — ")
                .Append(MarkdownV2Escaper.Escape(registration.Description))
                .Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }
}
