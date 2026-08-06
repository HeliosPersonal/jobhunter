using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;

namespace JobHunter.Telegram.Tests.Support;

/// <summary>
/// An <see cref="INotifier"/> that records every send in order, so the command suite can assert exactly what
/// a command replied with — the one line, the help list, the "not available yet" placeholder — and how many
/// messages it sent, with zero network. It is the command-path twin of the delivery suite's fake notifier.
/// </summary>
public sealed class RecordingNotifier : INotifier
{
    private readonly List<(long ChatId, RenderedMessage Message)> _sent = [];
    private long _nextMessageId = 5000;

    public IReadOnlyList<(long ChatId, RenderedMessage Message)> Sent => _sent;

    /// <summary>The body text of every message sent, in order.</summary>
    public IReadOnlyList<string> Texts => _sent.Select(s => s.Message.Text).ToList();

    /// <summary>The one message sent, or a failure if the count is not exactly one.</summary>
    public RenderedMessage OnlyMessage() => _sent.Count == 1
        ? _sent[0].Message
        : throw new InvalidOperationException($"Expected exactly one send, saw {_sent.Count}.");

    public Task<long> SendAsync(long chatId, RenderedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _sent.Add((chatId, message));
        return Task.FromResult(_nextMessageId++);
    }
}
