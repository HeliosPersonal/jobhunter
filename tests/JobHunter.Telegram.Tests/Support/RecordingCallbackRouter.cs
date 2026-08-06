using System.Collections.Concurrent;
using JobHunter.Telegram.Callbacks;
using JobHunter.Telegram.Transport;

namespace JobHunter.Telegram.Tests.Support;

/// <summary>
/// A hand-rolled spy for the internal <see cref="ICallbackRouter"/> — the processor suite records which
/// callbacks were routed to it, so a test can prove the gate routes an authorised callback and drops an
/// unauthorised one, without an NSubstitute proxy over an internal interface.
/// </summary>
internal sealed class RecordingCallbackRouter : ICallbackRouter
{
    private readonly ConcurrentQueue<TelegramCallbackQuery> _routed = new();

    public IReadOnlyCollection<TelegramCallbackQuery> Routed => _routed;

    public Task RouteAsync(TelegramCallbackQuery callback, CancellationToken cancellationToken = default)
    {
        _routed.Enqueue(callback);
        return Task.CompletedTask;
    }
}
