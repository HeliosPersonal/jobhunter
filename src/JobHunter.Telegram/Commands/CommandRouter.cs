using System.Collections.Frozen;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// The command dispatcher (T11, contract §Commands, ADR-F10-0002). It reads the leading <c>/token</c> of a
/// message, looks up the registered <see cref="ICommandHandler"/> and hands it the remaining arguments and
/// the Owner's chat id. Anything it does not recognise — an unknown command, a bare word, an empty line —
/// gets the nearest-command suggestion when a name is within two edits, otherwise the grouped list, and
/// nothing else: there is no conversational fallback and no LLM anywhere in this path, because a bot that
/// tries to chat sets an expectation this product does not meet (ADR-F10-0002, AC-09).
///
/// <para>Both the suggestion and the grouped fallback are projected from the descriptor catalogue — the same
/// single source the menu and <c>/help</c> derive from — so a routable command is always listed and a listed
/// command is always routable, and they cannot drift. Matching is case-insensitive and tolerates the
/// <c>@BotName</c> suffix Telegram appends to commands in groups.</para>
/// </summary>
internal sealed class CommandRouter
{
    private readonly FrozenDictionary<string, ICommandHandler> _handlers;
    private readonly IReadOnlyList<CommandDescriptor> _catalogue;
    private readonly ILogger<CommandRouter> _logger;

    public CommandRouter(
        IReadOnlyList<CommandRegistration> registrations,
        IReadOnlyList<CommandDescriptor> catalogue,
        ILogger<CommandRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _handlers = registrations.ToFrozenDictionary(
            r => r.Token, r => r.Handler, StringComparer.OrdinalIgnoreCase);
    }

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

        // Unknown command: the nearest command if one is close, else the grouped list — no chat, no LLM (AC-09).
        _logger.LogDebug("Unrecognised command; replied with a suggestion or the grouped list.");
        var reply = UnknownCommandFormatter.Reply(_catalogue, token ?? messageText);
        return [RenderedMessage.PlainText(reply)];
    }

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
}
