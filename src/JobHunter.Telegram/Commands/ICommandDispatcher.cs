namespace JobHunter.Telegram.Commands;

/// <summary>
/// The seam the singleton <see cref="Transport.OwnerGatedUpdateProcessor"/> dispatches an authorised
/// <c>/</c>-message through (T11). Command handlers read the store (a digest, saved roles, weekly stats), so
/// each dispatch runs in its own DI scope — exactly as the callback path does. Keeping this behind an
/// interface lets the processor's routing decision (message vs callback) stay a unit-tested singleton while
/// the scope glue lives in <see cref="ScopedCommandDispatcher"/>.
/// </summary>
internal interface ICommandDispatcher
{
    /// <summary>Routes <paramref name="messageText"/> for <paramref name="chatId"/> and sends the reply.</summary>
    Task DispatchAsync(long chatId, string messageText, CancellationToken cancellationToken = default);
}
