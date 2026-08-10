using System.Collections.Concurrent;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;

namespace JobHunter.Application.Tests.Ratings;

/// <summary>
/// A notifier that captures every rating prompt in order, so the weekly-loop tests can assert what was sent and
/// how many times. Each rendered prompt's <see cref="RenderedMessage.Text"/> carries its job id (the fake
/// renderer stamps it there), so the notifier records exactly what it received with no side channel.
/// </summary>
internal sealed class FakeNotifier : INotifier
{
    private readonly List<(long ChatId, RenderedMessage Message)> _sent = [];
    private long _nextMessageId = 2000;

    public IReadOnlyList<(long ChatId, RenderedMessage Message)> Sent => _sent;

    /// <summary>The prompt subjects that were actually sent, in order (each message's text is its job id).</summary>
    public IReadOnlyList<string> SentSubjects => _sent.Select(s => s.Message.Text).ToList();

    public Task<long> SendAsync(long chatId, RenderedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _sent.Add((chatId, message));
        return Task.FromResult(_nextMessageId++);
    }
}

/// <summary>
/// A renderer that stamps each card's job id into the prompt text, so the <see cref="FakeNotifier"/> can report
/// which cards were prompted without a side channel. The production renderer (facts + rating keyboard) arrives
/// later in the task; the handler under test only depends on getting one prompt per card.
/// </summary>
internal sealed class FakeWeeklyRatingRenderer : IWeeklyRatingRenderer
{
    public RenderedMessage Render(WeeklyTopCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return RenderedMessage.PlainText(card.JobId.ToString());
    }
}

/// <summary>
/// An in-memory rating-round log honouring the real unique <c>(week_start, chat_id)</c> constraint:
/// <see cref="TryOpenAsync"/> returns <c>false</c> when the round already exists, exactly as the database
/// arbitrates a redelivered tick. Rows are append-only, matching the append-only table.
/// </summary>
internal sealed class FakeRatingRoundLog : IRatingRoundLog
{
    private readonly ConcurrentDictionary<(DateTimeOffset, long), byte> _rounds = new();

    public IReadOnlyCollection<(DateTimeOffset WeekStart, long ChatId)> Rounds =>
        _rounds.Keys.ToList();

    public Task<bool> TryOpenAsync(
        DateTimeOffset weekStart, long chatId, DateTimeOffset openedAt, CancellationToken cancellationToken = default)
    {
        var opened = _rounds.TryAdd((weekStart, chatId), 0);
        return Task.FromResult(opened);
    }
}
