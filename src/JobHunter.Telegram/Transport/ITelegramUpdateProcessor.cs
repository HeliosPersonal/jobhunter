namespace JobHunter.Telegram.Transport;

/// <summary>
/// Handles one update the long-poll loop pulled (SAD §6.2). The implementation applies the
/// <see cref="Auth.OwnerAuthorizer"/> allowlist first — an update from any other chat is dropped before
/// routing (AC-10) — and then dispatches to the command surface. T07 wires the allowlist gate; the routing
/// behind it is filled in by later tasks (callbacks in T10, commands in T11).
/// </summary>
internal interface ITelegramUpdateProcessor
{
    Task ProcessAsync(TelegramUpdate update, CancellationToken cancellationToken = default);
}
