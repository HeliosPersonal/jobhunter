using JobHunter.Telegram.Auth;

namespace JobHunter.Telegram.Transport;

/// <summary>
/// The allowlist gate at the front of update processing (ADR-0014, SAD §6.2). Every update is checked
/// against <see cref="OwnerAuthorizer"/> before anything else happens: an update with no chat, or from a
/// chat that is not the Owner's, is dropped here and never reaches routing (AC-10). Routing for authorised
/// updates is added by later tasks (T10 callbacks, T11 commands); T07 establishes the gate and proves an
/// unauthorised update cannot pass it.
/// </summary>
internal sealed class OwnerGatedUpdateProcessor(OwnerAuthorizer authorizer, ILogger<OwnerGatedUpdateProcessor> logger)
    : ITelegramUpdateProcessor
{
    private readonly OwnerAuthorizer _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    private readonly ILogger<OwnerGatedUpdateProcessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task ProcessAsync(TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        // No chat id at all (an update kind we don't route) is treated as not the Owner's — dropped, not routed.
        if (update.ChatId is not { } chatId || !_authorizer.IsOwner(chatId))
        {
            return Task.CompletedTask;
        }

        // The Owner's update passed the gate. Routing is filled in by T10/T11; for now it is acknowledged.
        _logger.LogDebug("Accepted update {UpdateId} from the Owner's chat.", update.UpdateId);
        return Task.CompletedTask;
    }
}
