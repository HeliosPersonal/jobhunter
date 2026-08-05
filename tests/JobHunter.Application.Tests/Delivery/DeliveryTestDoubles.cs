using System.Collections.Concurrent;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;

namespace JobHunter.Application.Tests.Delivery;

/// <summary>
/// A notifier that captures every send in order (the test-plan's <c>FakeNotifier</c>), so the duplicate-delivery
/// suite can assert what was sent and how many times. Each rendered message's <see cref="RenderedMessage.Text"/>
/// carries its card key (the fake renderer stamps it there), so the notifier records exactly what it received —
/// no side channel. The per-key <see cref="BeforeSend"/> hook lets a test make one card refuse (a 400 →
/// <see cref="NotificationRejectedException"/>) or crash the process, and it fires <em>before</em> the send is
/// recorded so a refused card never counts as sent.
/// </summary>
internal sealed class FakeNotifier : INotifier
{
    private readonly List<(long ChatId, RenderedMessage Message)> _sent = [];
    private long _nextMessageId = 1000;

    /// <summary>Invoked with each message's card key (its text) before the send is recorded; throw to simulate a refusal or crash.</summary>
    public Action<string>? BeforeSend { get; set; }

    public IReadOnlyList<(long ChatId, RenderedMessage Message)> Sent => _sent;

    /// <summary>The card keys that were actually sent, in order (each message's text is its key).</summary>
    public IReadOnlyList<string> SentKeys => _sent.Select(s => s.Message.Text).ToList();

    public Task<long> SendAsync(long chatId, RenderedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        BeforeSend?.Invoke(message.Text);
        _sent.Add((chatId, message));
        return Task.FromResult(_nextMessageId++);
    }
}

/// <summary>
/// An in-memory <c>delivery_log</c> honouring the real unique <c>(run_id, chat_id, card_key)</c> constraint:
/// <see cref="TryRecordAsync"/> returns <c>false</c> when the row already exists, exactly as the database
/// arbitrates a racing double-send. Records are append-only, matching the append-only table. Two seams let the
/// suite reproduce the hard cases ADR-F5-0002 is about: <see cref="BeforeRecord"/> can throw to model a crash
/// in the one-statement window between the send and the log write, and <see cref="HideDeliveredKeys"/> makes
/// <see cref="DeliveredKeysAsync"/> report nothing while the rows still exist — a loser handler that read an
/// empty log, sent, and now finds every insert already taken.
/// </summary>
internal sealed class FakeDeliveryLog : IDeliveryLog
{
    private readonly ConcurrentDictionary<(Guid, long, string), DeliveryRecord> _rows = new();

    /// <summary>Fires just before a record is inserted; throw to model a process death in the send→log window.</summary>
    public Action<DeliveryRecord>? BeforeRecord { get; set; }

    /// <summary>When true, <see cref="DeliveredKeysAsync"/> reports an empty log though rows exist — the racing loser's view.</summary>
    public bool HideDeliveredKeys { get; set; }

    public IReadOnlyCollection<DeliveryRecord> Rows => _rows.Values.ToList();

    /// <summary>Seed a row as if an earlier pass had already delivered that key (for the resume and race tests).</summary>
    public void Seed(DeliveryRecord record) => _rows.TryAdd((record.RunId, record.ChatId, record.CardKey.Value), record);

    public Task<bool> TryRecordAsync(DeliveryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        BeforeRecord?.Invoke(record);
        var inserted = _rows.TryAdd((record.RunId, record.ChatId, record.CardKey.Value), record);
        return Task.FromResult(inserted);
    }

    public Task<IReadOnlyCollection<string>> DeliveredKeysAsync(
        Guid runId, long chatId, CancellationToken cancellationToken = default)
    {
        if (HideDeliveredKeys)
        {
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        }

        IReadOnlyCollection<string> keys = _rows.Keys
            .Where(k => k.Item1 == runId && k.Item2 == chatId)
            .Select(k => k.Item3)
            .ToList();
        return Task.FromResult(keys);
    }
}

/// <summary>
/// A renderer that returns a fixed, ordered sequence of keyed messages — header, one card per job key, then a
/// footer — with each message's text set to its key so the <see cref="FakeNotifier"/> can report what it sent
/// without a side channel. The production renderer (job facts + inline keyboards) arrives with T10/T12; the
/// handler under test only depends on getting an ordered, keyed sequence, so a keys-only stand-in is faithful.
/// </summary>
internal sealed class FakeDigestRenderer(IReadOnlyList<CardKey> keys) : IDigestRenderer
{
    public Task<IReadOnlyList<RenderableMessage>> RenderAsync(Digest digest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(digest);
        IReadOnlyList<RenderableMessage> messages = keys
            .Select(k => new RenderableMessage(k, RenderedMessage.PlainText(k.Value)))
            .ToList();
        return Task.FromResult(messages);
    }
}
