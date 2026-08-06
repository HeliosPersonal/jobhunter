using JobHunter.Telegram.Transport;

namespace JobHunter.Telegram.Callbacks;

/// <summary>
/// The seam between the singleton update processor and the scoped <see cref="CallbackHandler"/>: routing a
/// callback needs a fresh DI scope per tap (the action path reads and writes the store), which a singleton
/// cannot open for itself. The processor depends on this narrow router so its <em>routing decision</em> —
/// only the Owner's callbacks reach it (AC-10) — stays unit-tested, while the scope-per-callback glue lives
/// in one small implementation excluded from coverage.
/// </summary>
internal interface ICallbackRouter
{
    /// <summary>Opens a scope, resolves the <see cref="CallbackHandler"/> and handles one authorised callback.</summary>
    Task RouteAsync(TelegramCallbackQuery callback, CancellationToken cancellationToken = default);
}
