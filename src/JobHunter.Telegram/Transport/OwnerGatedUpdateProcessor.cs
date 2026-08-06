using JobHunter.Telegram.Auth;
using JobHunter.Telegram.Callbacks;

namespace JobHunter.Telegram.Transport;

/// <summary>
/// The allowlist gate at the front of update processing (ADR-0014, SAD §6.2). Every update is checked
/// against <see cref="OwnerAuthorizer"/> before anything else happens: an update with no chat, or from a
/// chat that is not the Owner's, is dropped here and never reaches routing (AC-10). An authorised callback
/// query is routed to the <see cref="ICallbackRouter"/> — the T10 card-action path; message routing (T11)
/// is still to come, so an authorised message is accepted and, for now, only logged.
/// </summary>
internal sealed class OwnerGatedUpdateProcessor(
    OwnerAuthorizer authorizer,
    ICallbackRouter callbackRouter,
    ILogger<OwnerGatedUpdateProcessor> logger)
    : ITelegramUpdateProcessor
{
    private readonly OwnerAuthorizer _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    private readonly ICallbackRouter _callbackRouter = callbackRouter ?? throw new ArgumentNullException(nameof(callbackRouter));
    private readonly ILogger<OwnerGatedUpdateProcessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task ProcessAsync(TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        // No chat id at all (an update kind we don't route) is treated as not the Owner's — dropped, not routed.
        if (update.ChatId is not { } chatId || !_authorizer.IsOwner(chatId))
        {
            return;
        }

        // A callback query is a card-action tap — route it through the scoped handler (T10). A message is a
        // command; its routing lands in T11, so for now the Owner's message is only acknowledged.
        if (update.CallbackQuery is { } callback)
        {
            await _callbackRouter.RouteAsync(callback, cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogDebug("Accepted update {UpdateId} from the Owner's chat.", update.UpdateId);
    }
}
