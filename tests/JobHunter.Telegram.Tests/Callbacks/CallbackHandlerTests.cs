using JobHunter.Application.Actions;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Callbacks;
using JobHunter.Telegram.Tests.Support;
using JobHunter.Telegram.Transport;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Callbacks;

/// <summary>
/// The callback half of T10 (AC-03, AC-08, AC-09, QG-3): a tap on a delivered card resolves its signed short
/// id, applies the action through the Application handler, acknowledges within a second and rewrites the
/// keyboard exactly as the contract fixes it ([[../contracts/telegram-messages|contract]] §Callback
/// payloads). The load-bearing properties, all proved here with zero network and a controllable clock: the
/// four ack texts and keyboards are verbatim (<c>Won't show similar</c> is D7 phrasing); a tap on a job that
/// no longer resolves or has closed says so plainly and records nothing invalid; a forged or unparseable
/// payload never crashes and never silently no-ops; and a double-tap is idempotent yet still re-acknowledges.
/// </summary>
public sealed class CallbackHandlerTests
{
    private const string Secret = "botfather-token-abc123";
    private const string ApplyUrl = "https://apply.example/roles/42";
    private static readonly Guid RunId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JobId = new("22222222-2222-2222-2222-222222222222");
    private const long ChatId = 4242;
    private const long MessageId = 9001;

    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly ISignalRepository _signals = Substitute.For<ISignalRepository>();
    private readonly ICardResolutionQuery _cards = Substitute.For<ICardResolutionQuery>();
    private readonly FakeClock _clock = new();

    private static readonly CardKey Key = CardKey.For(RunId, JobId);
    private static readonly CallbackDataCodec Codec =
        new(Options.Create(new TelegramOptions { BotToken = Secret, AllowedChatIds = [ChatId] }));
    private static readonly string ShortId = Codec.Encode(Key);

    private static JobFacts SampleFacts() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
    {
        [Dimension.Country] = ["DE"],
        [Dimension.Technology] = ["Kafka"],
    });

    private CallbackHandler Build(RecordingCallbackResponder responder, bool jobLive = true, bool firstTap = true)
    {
        _cards.CandidatesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new CardCandidate(Key, JobId, ApplyUrl)]);
        _facts.SnapshotAsync(JobId, Arg.Any<CancellationToken>()).Returns(jobLive ? SampleFacts() : null);
        _signals.TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>()).Returns(firstTap);

        var action = new RecordCardActionHandler(_facts, _signals, new SequentialIdGenerator());
        return new CallbackHandler(
            _cards, Codec, action, responder, _clock, TimeSpan.FromDays(7),
            NullLogger<CallbackHandler>.Instance);
    }

    private static TelegramCallbackQuery Tap(string token, string shortId = null!, string queryId = "cb1") =>
        new(queryId, $"{token}:{shortId ?? ShortId}", new TelegramMessage(new TelegramChat(ChatId), null, MessageId));

    [Fact]
    public async Task Ignore_captures_the_signal_acknowledges_the_D7_phrasing_and_shows_the_ignored_keyboard()
    {
        var responder = new RecordingCallbackResponder();
        Signal? captured = null;
        _signals.TryCaptureAsync(Arg.Do<Signal>(s => captured = s), Arg.Any<CancellationToken>()).Returns(true);

        await Build(responder).HandleAsync(Tap("ign"), CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Kind.ShouldBe(SignalKind.Ignored);
        responder.LastAck!.Value.Text.ShouldBe("Won't show similar");
        var keyboard = responder.LastEdit!.Value.Keyboard;
        keyboard.ShouldHaveSingleItem();
        var only = keyboard[0].ShouldHaveSingleItem();
        only.Label.ShouldBe("Ignored");
        only.CallbackData.ShouldBe($"ign:{ShortId}");
    }

    [Fact]
    public async Task Save_captures_the_signal_acknowledges_and_shows_the_saved_keyboard()
    {
        var responder = new RecordingCallbackResponder();
        Signal? captured = null;
        _signals.TryCaptureAsync(Arg.Do<Signal>(s => captured = s), Arg.Any<CancellationToken>()).Returns(true);

        await Build(responder).HandleAsync(Tap("sav"), CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Kind.ShouldBe(SignalKind.Saved);
        responder.LastAck!.Value.Text.ShouldBe("Saved");
        var row = responder.LastEdit!.Value.Keyboard.ShouldHaveSingleItem();
        row.Count.ShouldBe(3);
        row[0].Label.ShouldBe("Open");
        row[0].Url.ShouldBe(ApplyUrl);
        row[0].CallbackData.ShouldBeNull();
        row[1].Label.ShouldBe("Saved ✓");
        row[1].CallbackData.ShouldBe($"sav:{ShortId}");
        row[2].Label.ShouldBe("Applied");
        row[2].CallbackData.ShouldBe($"app:{ShortId}");
    }

    [Fact]
    public async Task Applied_acknowledges_and_shows_the_applied_keyboard_but_writes_no_F5_signal()
    {
        var responder = new RecordingCallbackResponder();

        await Build(responder).HandleAsync(Tap("app"), CancellationToken.None);

        responder.LastAck!.Value.Text.ShouldBe("Marked as applied");
        var row = responder.LastEdit!.Value.Keyboard.ShouldHaveSingleItem();
        row.Count.ShouldBe(2);
        row[0].Label.ShouldBe("Open");
        row[0].Url.ShouldBe(ApplyUrl);
        row[1].Label.ShouldBe("Applied ✓");
        row[1].CallbackData.ShouldBe($"app:{ShortId}");
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rating_tap_captures_a_Rated_signal_resolved_from_the_payload_not_the_window()
    {
        var responder = new RecordingCallbackResponder();
        Signal? captured = null;
        _signals.TryCaptureAsync(Arg.Do<Signal>(s => captured = s), Arg.Any<CancellationToken>()).Returns(true);
        var handler = Build(responder);
        var tap = new TelegramCallbackQuery(
            "cb1", $"rat:{Codec.EncodeRating(JobId)}", new TelegramMessage(new TelegramChat(ChatId), null, MessageId));

        await handler.HandleAsync(tap, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.JobId.ShouldBe(JobId);
        captured.Kind.ShouldBe(SignalKind.Rated);
        // The rating resolves from its self-contained signed payload — never through the windowed card query.
        await _cards.DidNotReceive().CandidatesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rating_tap_acknowledges_and_shows_the_rated_keyboard()
    {
        var responder = new RecordingCallbackResponder();
        var handler = Build(responder);
        var tap = new TelegramCallbackQuery(
            "cb1", $"rat:{Codec.EncodeRating(JobId)}", new TelegramMessage(new TelegramChat(ChatId), null, MessageId));

        await handler.HandleAsync(tap, CancellationToken.None);

        responder.LastAck!.Value.Text.ShouldBe("Thanks — noted");
        var row = responder.LastEdit!.Value.Keyboard.ShouldHaveSingleItem();
        var only = row.ShouldHaveSingleItem();
        only.Label.ShouldBe("Rated 👍");
    }

    [Fact]
    public async Task A_forged_rating_payload_says_so_plainly_and_records_nothing()
    {
        var responder = new RecordingCallbackResponder();
        var forged = Build2("a-different-secret").EncodeRating(JobId);
        var tap = new TelegramCallbackQuery(
            "cb1", $"rat:{forged}", new TelegramMessage(new TelegramChat(ChatId), null, MessageId));

        await Build(responder).HandleAsync(tap, CancellationToken.None);

        responder.LastAck!.Value.Text.ShouldBe("This role has closed");
        responder.Edits.ShouldBeEmpty();
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
    }

    // A codec under a different secret, to forge a rating payload the real codec must reject.
    private static CallbackDataCodec Build2(string secret) =>
        new(Options.Create(new TelegramOptions { BotToken = secret, AllowedChatIds = [ChatId] }));

    [Fact]
    public async Task Open_acknowledges_without_a_toast_and_leaves_the_keyboard_untouched()
    {
        var responder = new RecordingCallbackResponder();

        await Build(responder).HandleAsync(Tap("open"), CancellationToken.None);

        responder.Acks.ShouldHaveSingleItem();
        responder.LastAck!.Value.Text.ShouldBeNull();
        responder.Edits.ShouldBeEmpty();
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_acknowledgement_happens_within_one_second_of_the_tap()
    {
        var responder = new RecordingCallbackResponder(_clock);
        var start = _clock.UtcNow;

        await Build(responder).HandleAsync(Tap("sav"), CancellationToken.None);

        var ackedAt = responder.LastAck!.Value.At.ShouldNotBeNull();
        (ackedAt - start).ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_tap_on_a_closed_job_says_so_plainly_and_records_nothing()
    {
        var responder = new RecordingCallbackResponder();

        await Build(responder, jobLive: false).HandleAsync(Tap("sav"), CancellationToken.None);

        responder.LastAck!.Value.Text.ShouldBe("This role has closed");
        responder.Edits.ShouldBeEmpty();
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_short_id_that_no_longer_resolves_says_so_plainly_and_never_touches_the_action()
    {
        var responder = new RecordingCallbackResponder();

        await Build(responder).HandleAsync(Tap("sav", shortId: "unresolvable"), CancellationToken.None);

        responder.LastAck!.Value.Text.ShouldBe("This role has closed");
        responder.Edits.ShouldBeEmpty();
        await _facts.DidNotReceive().SnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("nope:")]
    [InlineData("xyz:something")]
    public async Task A_forged_or_unparseable_payload_gives_a_clear_message_and_never_a_silent_no_op(string data)
    {
        var responder = new RecordingCallbackResponder();
        var callback = new TelegramCallbackQuery("cb1", data, new TelegramMessage(new TelegramChat(ChatId), null, MessageId));

        await Build(responder).HandleAsync(callback, CancellationToken.None);

        responder.Acks.ShouldHaveSingleItem();
        responder.LastAck!.Value.Text.ShouldBe("This role has closed");
        responder.Edits.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_identical_tap_is_idempotent_yet_still_re_acknowledges()
    {
        var responder = new RecordingCallbackResponder();

        // The signal already exists (TryCaptureAsync returns false) — the outcome is AlreadyCaptured.
        await Build(responder, firstTap: false).HandleAsync(Tap("sav"), CancellationToken.None);

        responder.LastAck!.Value.Text.ShouldBe("Saved");
        responder.LastEdit.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_callback_without_a_message_is_acknowledged_but_edits_nothing()
    {
        var responder = new RecordingCallbackResponder();
        var callback = new TelegramCallbackQuery("cb1", $"sav:{ShortId}", null);

        await Build(responder).HandleAsync(callback, CancellationToken.None);

        responder.LastAck!.Value.Text.ShouldBe("This role has closed");
        responder.Edits.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_null_callback_is_rejected()
    {
        var handler = Build(new RecordingCallbackResponder());

        await Should.ThrowAsync<ArgumentNullException>(() => handler.HandleAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var responder = new RecordingCallbackResponder();
        var action = new RecordCardActionHandler(_facts, _signals, new SequentialIdGenerator());
        var log = NullLogger<CallbackHandler>.Instance;

        Should.Throw<ArgumentNullException>(() => new CallbackHandler(null!, Codec, action, responder, _clock, TimeSpan.FromDays(7), log));
        Should.Throw<ArgumentNullException>(() => new CallbackHandler(_cards, null!, action, responder, _clock, TimeSpan.FromDays(7), log));
        Should.Throw<ArgumentNullException>(() => new CallbackHandler(_cards, Codec, null!, responder, _clock, TimeSpan.FromDays(7), log));
        Should.Throw<ArgumentNullException>(() => new CallbackHandler(_cards, Codec, action, null!, _clock, TimeSpan.FromDays(7), log));
        Should.Throw<ArgumentNullException>(() => new CallbackHandler(_cards, Codec, action, responder, null!, TimeSpan.FromDays(7), log));
        Should.Throw<ArgumentNullException>(() => new CallbackHandler(_cards, Codec, action, responder, _clock, TimeSpan.FromDays(7), null!));
    }
}
