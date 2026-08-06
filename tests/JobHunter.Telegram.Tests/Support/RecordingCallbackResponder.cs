using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Callbacks;

namespace JobHunter.Telegram.Tests.Support;

/// <summary>
/// A hand-rolled spy for the internal <see cref="ICallbackResponder"/> — the callback suite records the ack
/// text and the keyboard edit a tap produced, without an NSubstitute proxy over an internal interface (the
/// same reason <see cref="RecordingUpdateProcessor"/> is hand-rolled). It only records; it never talks to
/// Telegram. An optional <see cref="IClock"/> lets a test stamp the instant of each ack, so the QG-3 latency
/// ceiling can be asserted deterministically without a real wait.
/// </summary>
internal sealed class RecordingCallbackResponder(IClock? clock = null) : ICallbackResponder
{
    private readonly IClock? _clock = clock;
    private readonly List<(string QueryId, string? Text, DateTimeOffset? At)> _acks = [];
    private readonly List<(long ChatId, long MessageId, IReadOnlyList<IReadOnlyList<InlineButton>> Keyboard)> _edits = [];

    public IReadOnlyList<(string QueryId, string? Text, DateTimeOffset? At)> Acks => _acks;
    public IReadOnlyList<(long ChatId, long MessageId, IReadOnlyList<IReadOnlyList<InlineButton>> Keyboard)> Edits => _edits;

    public (string QueryId, string? Text, DateTimeOffset? At)? LastAck => _acks.Count > 0 ? _acks[^1] : null;
    public (long ChatId, long MessageId, IReadOnlyList<IReadOnlyList<InlineButton>> Keyboard)? LastEdit =>
        _edits.Count > 0 ? _edits[^1] : null;

    public Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken cancellationToken = default)
    {
        _acks.Add((callbackQueryId, text, _clock?.UtcNow));
        return Task.CompletedTask;
    }

    public Task EditReplyMarkupAsync(
        long chatId,
        long messageId,
        IReadOnlyList<IReadOnlyList<InlineButton>> keyboard,
        CancellationToken cancellationToken = default)
    {
        _edits.Add((chatId, messageId, keyboard));
        return Task.CompletedTask;
    }
}
