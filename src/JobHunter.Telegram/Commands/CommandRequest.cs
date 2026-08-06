namespace JobHunter.Telegram.Commands;

/// <summary>
/// One dispatched command: the Owner's chat id and the arguments that followed the <c>/token</c>, already
/// split off by the <see cref="CommandRouter"/> (T11). The chat id has already passed the allowlist gate
/// (<see cref="Auth.OwnerAuthorizer"/>) before a request is ever built, so a handler never re-checks
/// authorisation — it only ever runs for the Owner (invariant 9).
/// </summary>
/// <param name="ChatId">The Owner's chat, the reply target and the idempotence key half delivery uses.</param>
/// <param name="Arguments">Everything after the command token, trimmed; null when the token stood alone.</param>
public sealed record CommandRequest(long ChatId, string? Arguments);
