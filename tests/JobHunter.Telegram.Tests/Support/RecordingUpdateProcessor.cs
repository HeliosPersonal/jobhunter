using System.Collections.Concurrent;
using JobHunter.Telegram.Transport;

namespace JobHunter.Telegram.Tests.Support;

/// <summary>
/// A hand-rolled spy for the internal <see cref="ITelegramUpdateProcessor"/> — the long-poll suite records
/// which updates were handed to it and in what order, without an NSubstitute proxy over an internal
/// interface. An optional throw-once hook lets a test simulate a handler fault; the default just records.
/// </summary>
internal sealed class RecordingUpdateProcessor : ITelegramUpdateProcessor
{
    private readonly ConcurrentQueue<long> _processed = new();

    public IReadOnlyCollection<long> Processed => _processed;

    public Task ProcessAsync(TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        _processed.Enqueue(update.UpdateId);
        return Task.CompletedTask;
    }
}
