using JobHunter.Application.Delivery;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using DeliveryOptions = JobHunter.Application.Delivery.DeliveryOptions;

namespace JobHunter.Application.Tests.Delivery;

/// <summary>
/// T08/T09: the delivery step and the whole reason [[adr/0002-delivery-idempotence|ADR-F5-0002]] exists — the
/// duplicate-delivery suite (QG-2, invariant 8). The handler is fired by the 07:00 slot
/// (<see cref="DigestDeliveryDue"/>, T09): it resolves the day's Run — the live one, else the most recent
/// terminal one — loads that Run's stored digest, renders it into an ordered keyed sequence, asks the
/// <c>delivery_log</c> what already went to the chat, and sends only the remainder, writing a log row
/// <em>immediately after each successful send</em>. The load-bearing properties: a clean run of ten cards
/// sends twelve messages and writes twelve rows; a crash after card three resumes to send exactly the
/// remaining seven and never a card twice; a crash in the one-statement send→log window re-sends that one
/// card (at-least-once, deliberately); a retry after success sends nothing; two racing handlers cannot
/// double-send because the unique constraint arbitrates; a per-card 400 logs that one card failed while the
/// rest deliver (AC-05); and when no Run exists at all the slot is genuine silence the handler does not paper
/// over (R1). Every collaborator is faked, so these are zero-database, zero-network unit tests.
/// </summary>
public sealed class DeliveryHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000C8");

    private readonly FakeNotifier _notifier = new();
    private readonly FakeDeliveryLog _log = new();
    private readonly SequentialIdGenerator _ids = new();
    private readonly FakeClock _clock = new(Now);
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IDigestRepository _digests = Substitute.For<IDigestRepository>();
    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();

    public DeliveryHandlerTests()
    {
        // The default day: a live Run whose id the digest and the log are keyed by. A test that wants the
        // terminal-Run or no-Run path overrides these two arranges.
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns(RunForId(RunId));
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(BuildDigest());
    }

    private static Run RunForId(Guid id) => new(id, Now.AddDays(-1), Now, ceilingUsd: 5m, Now);

    // ---- the ordered, keyed sequence a delivery renders: header, N cards, footer ----------------

    private static List<CardKey> Sequence(int cardCount)
    {
        var keys = new List<CardKey> { CardKey.Header };
        for (var i = 0; i < cardCount; i++)
        {
            keys.Add(CardKey.For(RunId, Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}")));
        }

        keys.Add(CardKey.Footer);
        return keys;
    }

    private static Digest BuildDigest() => BuildDigestForRun(RunId);

    private static Digest BuildDigestForRun(Guid runId) =>
        new(Guid.Parse("00000000-0000-0000-0000-0000000000D9"), runId, DigestMode.Full, totalNewJobs: 0,
            strongMatches: 0, avgSalaryUsd: null, suppressedCount: 0, suppressionBreakdown: [], carriedOverCount: 0,
            companiesChecked: 0, analysedCount: 0, degradedSources: [], narrative: null, NarrativeSource.Template,
            promptVersion: null, cards: [], generatedAt: Now);

    private DeliveryHandler CreateHandler(IReadOnlyList<CardKey> sequence) =>
        new(_runs, _digests, new FakeDigestRenderer(sequence), _log, _notifier, _ids, _clock,
            new DeliveryOptions { OwnerChatId = OwnerChat }, NullLogger<DeliveryHandler>.Instance);

    private static DigestDeliveryDue Message() => new(Now);

    private DigestDelivered? Delivered() =>
        _bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMessageBus.PublishAsync))
            .Select(c => c.GetArguments()[0])
            .OfType<DigestDelivered>()
            .SingleOrDefault();

    // ---- clean delivery -------------------------------------------------------------------------

    [Fact]
    public async Task A_clean_delivery_of_ten_cards_sends_twelve_messages_and_writes_twelve_rows()
    {
        var sequence = Sequence(10);

        await CreateHandler(sequence).Handle(Message(), _bus, CancellationToken.None);

        // Header + 10 cards + footer, each sent once and logged once (invariant 8).
        _notifier.SentKeys.Count.ShouldBe(12);
        _log.Rows.Count.ShouldBe(12);
        _notifier.SentKeys.ShouldBe(sequence.Select(k => k.Value));
        Delivered().ShouldNotBeNull().MessagesSent.ShouldBe(12);
        Delivered()!.CardsFailed.ShouldBe(0);
    }

    [Fact]
    public async Task Every_send_is_recorded_to_the_chat_the_options_name()
    {
        await CreateHandler(Sequence(3)).Handle(Message(), _bus, CancellationToken.None);

        _notifier.Sent.ShouldAllBe(s => s.ChatId == OwnerChat);
        _log.Rows.ShouldAllBe(r => r.ChatId == OwnerChat && r.RunId == RunId);
    }

    // ---- kill after card 3, restart -------------------------------------------------------------

    [Fact]
    public async Task Killing_after_card_three_and_restarting_sends_exactly_the_remaining_seven()
    {
        var sequence = Sequence(10);
        // First pass dies after the third card is sent and logged: header, card1, card2, card3 → 4 rows.
        var sentInFirstPass = 0;
        _notifier.BeforeSend = _ =>
        {
            if (sentInFirstPass == 4)
            {
                throw new OperationCanceledException("process killed after card 3");
            }

            sentInFirstPass++;
        };

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateHandler(sequence).Handle(Message(), _bus, CancellationToken.None));
        _log.Rows.Count.ShouldBe(4);

        // Restart on a fresh handler with a live notifier: only the untouched remainder should go.
        var secondNotifier = new FakeNotifier();
        var resumed = new DeliveryHandler(_runs, _digests, new FakeDigestRenderer(sequence), _log, secondNotifier,
            _ids, _clock, new DeliveryOptions { OwnerChatId = OwnerChat }, NullLogger<DeliveryHandler>.Instance);

        await resumed.Handle(Message(), _bus, CancellationToken.None);

        // Exactly the remaining seven cards + footer = 8 messages this pass; 12 rows total; no card twice.
        secondNotifier.SentKeys.Count.ShouldBe(8);
        secondNotifier.SentKeys.ShouldBe(sequence.Skip(4).Select(k => k.Value));
        _log.Rows.Count.ShouldBe(12);
        _log.Rows.Select(r => r.CardKey.Value).Distinct().Count().ShouldBe(12);
    }

    // ---- kill between send and log write --------------------------------------------------------

    [Fact]
    public async Task A_crash_between_send_and_log_resends_that_one_card_on_resume()
    {
        var sequence = Sequence(3);
        // The header sends, then the process dies before its log row is written — the one-statement window.
        _log.BeforeRecord = record =>
        {
            if (record.CardKey.Value == CardKey.HeaderValue)
            {
                throw new OperationCanceledException("killed between send and log");
            }
        };

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateHandler(sequence).Handle(Message(), _bus, CancellationToken.None));
        _notifier.SentKeys.ShouldBe([CardKey.HeaderValue]);
        _log.Rows.ShouldBeEmpty();

        // Resume with the window closed: the header is re-sent (the duplicate ADR-F5-0002 accepts) and the rest follow.
        _log.BeforeRecord = null;
        var secondNotifier = new FakeNotifier();
        var resumed = new DeliveryHandler(_runs, _digests, new FakeDigestRenderer(sequence), _log, secondNotifier,
            _ids, _clock, new DeliveryOptions { OwnerChatId = OwnerChat }, NullLogger<DeliveryHandler>.Instance);

        await resumed.Handle(Message(), _bus, CancellationToken.None);

        // Header re-sent once (at-least-once), then cards and footer; the header is the single duplicate.
        secondNotifier.SentKeys.ShouldBe(sequence.Select(k => k.Value));
        _log.Rows.Count.ShouldBe(sequence.Count);
    }

    // ---- retry after success --------------------------------------------------------------------

    [Fact]
    public async Task Retrying_a_completed_delivery_sends_nothing()
    {
        var sequence = Sequence(10);
        await CreateHandler(sequence).Handle(Message(), _bus, CancellationToken.None);
        var rowsAfterFirst = _log.Rows.Count;

        var secondNotifier = new FakeNotifier();
        var retry = new DeliveryHandler(_runs, _digests, new FakeDigestRenderer(sequence), _log, secondNotifier,
            _ids, _clock, new DeliveryOptions { OwnerChatId = OwnerChat }, NullLogger<DeliveryHandler>.Instance);

        await retry.Handle(Message(), _bus, CancellationToken.None);

        // A redelivered DigestReady finds every card in the log and sends nothing new (QG-2).
        secondNotifier.SentKeys.ShouldBeEmpty();
        _log.Rows.Count.ShouldBe(rowsAfterFirst);
        // It still re-emits the completion so a lost first event is recoverable — with a zero send count.
        var completions = _bus.ReceivedCalls()
            .Select(c => c.GetArguments()[0]).OfType<DigestDelivered>().ToList();
        completions.Count.ShouldBe(2);
        completions[^1].MessagesSent.ShouldBe(0);
    }

    // ---- two racing handlers --------------------------------------------------------------------

    [Fact]
    public async Task Two_racing_handlers_send_each_card_once_because_the_constraint_arbitrates()
    {
        var sequence = Sequence(3);
        // Both handlers read an empty log and both send every message — the classic race. The unique
        // constraint is the arbiter: only the first insert of each key wins, so total *counted* sends
        // equal the card count even though both notifiers physically sent.
        _log.HideDeliveredKeys = true;

        var notifierA = new FakeNotifier();
        var notifierB = new FakeNotifier();
        var handlerA = new DeliveryHandler(_runs, _digests, new FakeDigestRenderer(sequence), _log, notifierA,
            _ids, _clock, new DeliveryOptions { OwnerChatId = OwnerChat }, NullLogger<DeliveryHandler>.Instance);
        var handlerB = new DeliveryHandler(_runs, _digests, new FakeDigestRenderer(sequence), _log, notifierB,
            _ids, _clock, new DeliveryOptions { OwnerChatId = OwnerChat }, NullLogger<DeliveryHandler>.Instance);

        await handlerA.Handle(Message(), _bus, CancellationToken.None);
        await handlerB.Handle(Message(), _bus, CancellationToken.None);

        // The log holds exactly one row per key — the constraint let no key be recorded twice.
        _log.Rows.Count.ShouldBe(sequence.Count);
        _log.Rows.Select(r => r.CardKey.Value).Distinct().Count().ShouldBe(sequence.Count);
        // The winners sum to the card count; the loser's TryRecord returned false and was not counted.
        var delivered = _bus.ReceivedCalls()
            .Select(c => c.GetArguments()[0]).OfType<DigestDelivered>().ToList();
        delivered.Sum(d => d.MessagesSent).ShouldBe(sequence.Count);
    }

    // ---- a 400 on one card ----------------------------------------------------------------------

    [Fact]
    public async Task A_permanent_rejection_on_one_card_logs_it_failed_and_delivers_the_rest()
    {
        var sequence = Sequence(3);
        var poison = sequence[2].Value; // the second card refuses
        _notifier.BeforeSend = key =>
        {
            if (key == poison)
            {
                throw new NotificationRejectedException("Telegram rejected the card (400).");
            }
        };

        await CreateHandler(sequence).Handle(Message(), _bus, CancellationToken.None);

        // The rejected card is not sent and writes no row, but every other message delivers (AC-05).
        _notifier.SentKeys.ShouldNotContain(poison);
        _log.Rows.Select(r => r.CardKey.Value).ShouldNotContain(poison);
        _log.Rows.Count.ShouldBe(sequence.Count - 1);
        var delivered = Delivered().ShouldNotBeNull();
        delivered.MessagesSent.ShouldBe(sequence.Count - 1);
        delivered.CardsFailed.ShouldBe(1);
    }

    [Fact]
    public async Task A_rejected_card_is_not_logged_so_a_later_valid_render_can_still_deliver_it()
    {
        var sequence = Sequence(2);
        var poison = sequence[1].Value; // the single card refuses on the first pass
        _notifier.BeforeSend = key =>
        {
            if (key == poison)
            {
                throw new NotificationRejectedException("Telegram rejected the card (400).");
            }
        };

        await CreateHandler(sequence).Handle(Message(), _bus, CancellationToken.None);
        _log.Rows.Select(r => r.CardKey.Value).ShouldNotContain(poison);

        // A later pass with a valid message (no refusal) delivers the card that was refused before.
        var secondNotifier = new FakeNotifier();
        var resumed = new DeliveryHandler(_runs, _digests, new FakeDigestRenderer(sequence), _log, secondNotifier,
            _ids, _clock, new DeliveryOptions { OwnerChatId = OwnerChat }, NullLogger<DeliveryHandler>.Instance);

        await resumed.Handle(Message(), _bus, CancellationToken.None);

        secondNotifier.SentKeys.ShouldBe([poison]);
        _log.Rows.Select(r => r.CardKey.Value).ShouldContain(poison);
    }

    // ---- transient fault propagates -------------------------------------------------------------

    [Fact]
    public async Task A_transient_send_fault_propagates_so_wolverine_redelivers()
    {
        var sequence = Sequence(3);
        _notifier.BeforeSend = key =>
        {
            if (key == sequence[1].Value)
            {
                throw new TelegramLikeTransientException("connection reset");
            }
        };

        // A transient fault is not a per-card rejection: it propagates so the message is retried from the log.
        await Should.ThrowAsync<TelegramLikeTransientException>(
            () => CreateHandler(sequence).Handle(Message(), _bus, CancellationToken.None));

        // The header was sent and logged before the fault; the loop stopped there, no completion published.
        _log.Rows.Count.ShouldBe(1);
        Delivered().ShouldBeNull();
    }

    // ---- guards ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_missing_digest_is_surfaced_so_wolverine_retries_once_the_write_is_visible()
    {
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns((Digest?)null);

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateHandler(Sequence(1)).Handle(Message(), _bus, CancellationToken.None));

        _notifier.SentKeys.ShouldBeEmpty();
        _log.Rows.ShouldBeEmpty();
    }

    // ---- T09: resolving the day's Run from the 07:00 slot ----------------------------------------

    [Fact]
    public async Task The_slot_delivers_the_most_recent_run_when_the_day_has_already_gone_terminal()
    {
        // A degraded day (CostAborted) finishes before 07:00, so there is no live Run — the slot must still
        // find the day's Run through the most-recent lookup and deliver its reduced digest (ADR-F5-0001).
        var terminalId = Guid.Parse("00000000-0000-0000-0000-0000000000CA");
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns(RunForId(terminalId));
        _digests.FindByRunAsync(terminalId, Arg.Any<CancellationToken>())
            .Returns(BuildDigestForRun(terminalId));

        await CreateHandler(Sequence(2)).Handle(Message(), _bus, CancellationToken.None);

        _notifier.SentKeys.Count.ShouldBe(4); // header + 2 cards + footer
        _log.Rows.ShouldAllBe(r => r.RunId == terminalId);
        Delivered().ShouldNotBeNull().RunId.ShouldBe(terminalId);
    }

    [Fact]
    public async Task No_run_at_all_is_silence_the_slot_does_not_paper_over()
    {
        // The 02:00 tick never fired, so no Run row exists. That is genuine infrastructure silence the R1
        // runbook alerts on — the handler sends nothing and publishes nothing rather than inventing a digest.
        _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);

        await CreateHandler(Sequence(1)).Handle(Message(), _bus, CancellationToken.None);

        _notifier.SentKeys.ShouldBeEmpty();
        _log.Rows.ShouldBeEmpty();
        Delivered().ShouldBeNull();
        await _digests.DidNotReceive().FindByRunAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_null_message_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => CreateHandler(Sequence(1)).Handle(null!, _bus, CancellationToken.None));
    }

    [Fact]
    public async Task A_null_bus_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => CreateHandler(Sequence(1)).Handle(Message(), null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("runs")]
    [InlineData("digests")]
    [InlineData("renderer")]
    [InlineData("deliveryLog")]
    [InlineData("notifier")]
    [InlineData("ids")]
    [InlineData("clock")]
    [InlineData("options")]
    [InlineData("logger")]
    public void A_null_dependency_is_rejected(string nullDependency)
    {
        IRunRepository? runs = _runs;
        IDigestRepository? digests = _digests;
        IDigestRenderer? renderer = new FakeDigestRenderer(Sequence(1));
        IDeliveryLog? log = _log;
        INotifier? notifier = _notifier;
        IIdGenerator? ids = _ids;
        IClock? clock = _clock;
        DeliveryOptions? options = new() { OwnerChatId = OwnerChat };
        ILogger<DeliveryHandler>? logger = NullLogger<DeliveryHandler>.Instance;

        switch (nullDependency)
        {
            case "runs": runs = null; break;
            case "digests": digests = null; break;
            case "renderer": renderer = null; break;
            case "deliveryLog": log = null; break;
            case "notifier": notifier = null; break;
            case "ids": ids = null; break;
            case "clock": clock = null; break;
            case "options": options = null; break;
            case "logger": logger = null; break;
        }

        Should.Throw<ArgumentNullException>(() =>
            new DeliveryHandler(runs!, digests!, renderer!, log!, notifier!, ids!, clock!, options!, logger!));
    }

    /// <summary>A stand-in for the transport-fault type the real notifier throws on a 5xx or dropped connection.</summary>
    private sealed class TelegramLikeTransientException(string message) : Exception(message);
}
