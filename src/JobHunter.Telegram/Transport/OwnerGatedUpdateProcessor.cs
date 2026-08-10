using JobHunter.Telegram.Auth;
using JobHunter.Telegram.Callbacks;
using JobHunter.Telegram.Commands;

namespace JobHunter.Telegram.Transport;

/// <summary>
/// The allowlist gate at the front of update processing (ADR-0014, SAD §6.2). Every update is checked
/// against <see cref="OwnerAuthorizer"/> before anything else happens: an update with no chat, or from a
/// chat that is not the Owner's, is dropped here and never reaches routing (AC-10). An authorised callback
/// query is routed to the <see cref="ICallbackRouter"/> — the T10 card-action path; an authorised message
/// whose text begins with <c>/</c> is a command, dispatched through the <see cref="ICommandDispatcher"/> —
/// the T11 command path. Anything else the Owner sends is accepted and only logged, because this product has
/// no conversational surface (ADR-F10-0002).
/// </summary>
internal sealed class OwnerGatedUpdateProcessor(
    OwnerAuthorizer authorizer,
    ICallbackRouter callbackRouter,
    ICommandDispatcher commandDispatcher,
    ILogger<OwnerGatedUpdateProcessor> logger)
    : ITelegramUpdateProcessor
{
    private readonly OwnerAuthorizer _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    private readonly ICallbackRouter _callbackRouter = callbackRouter ?? throw new ArgumentNullException(nameof(callbackRouter));
    private readonly ICommandDispatcher _commandDispatcher = commandDispatcher ?? throw new ArgumentNullException(nameof(commandDispatcher));
    private readonly ILogger<OwnerGatedUpdateProcessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task ProcessAsync(TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        // No chat id at all (an update kind we don't route) is treated as not the Owner's — dropped, not routed.
        if (update.ChatId is not { } chatId || !_authorizer.IsOwner(chatId))
        {
            return;
        }

        // A callback query is a card-action tap — route it through the scoped handler (T10).
        if (update.CallbackQuery is { } callback)
        {
            await _callbackRouter.RouteAsync(callback, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Any message the Owner sends that carries text is dispatched through the scoped command path. A
        // leading '/' is a command; anything else may be a free-text reply resuming a pending multi-step command
        // (a /note awaiting its body). The dispatcher's conversation coordinator decides which — either it
        // resumes a pending command or the router answers it as an unknown command; there is no conversational
        // fallback here (T10 S5, ADR-F10-0002).
        if (update.Message?.Text is { } text)
        {
            await _commandDispatcher.DispatchAsync(chatId, text, cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogDebug("Accepted a non-text update {UpdateId} from the Owner's chat.", update.UpdateId);
    }
}
