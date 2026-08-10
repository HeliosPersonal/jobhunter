using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/redeliver</c> (catalogue §Operations · Sensitive · ✎): re-delivers today's digest, safe by construction —
/// the delivery log means an already-sent card is never sent again (ADR-F5-0002). The confirmation states how
/// <em>many cards would actually go out</em>, usually zero, which is the point. It resolves the day's Run as
/// delivery does, renders the digest, and set-differences the rendered card keys against the delivery log to get
/// the would-be-sent count. State-changing: it previews and stores a pending <see cref="ConversationState"/>; the
/// confirm tap that actually publishes the redelivery is wired in T10, exactly as <c>/floor</c>'s confirm is.
/// </summary>
public sealed class RedeliverCommandHandlerTests
{
    private const long OwnerChat = 4242;
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly IDigestRepository _digests = Substitute.For<IDigestRepository>();
    private readonly IDigestRenderer _renderer = Substitute.For<IDigestRenderer>();
    private readonly IDeliveryLog _deliveryLog = Substitute.For<IDeliveryLog>();
    private readonly IConversationStateStore _state = Substitute.For<IConversationStateStore>();
    private readonly IOperationScheduler _scheduler = Substitute.For<IOperationScheduler>();
    private readonly FakeClock _clock = new(Now);

    private RedeliverCommandHandler NewHandler() =>
        new(_runs, _digests, _renderer, _deliveryLog, _state, _scheduler, _clock, NullLogger<RedeliverCommandHandler>.Instance);

    private static CommandResumeRequest ConfirmResume(string input) =>
        new(OwnerChat, "confirm",
            new Dictionary<string, string> { ["runId"] = RunId.ToString("D") }, input);

    private static Run DeliveredRun()
    {
        var run = new Run(RunId, Now.AddHours(-3), Now.AddHours(-2), ceilingUsd: 5.00m, Now.AddHours(-3));
        run.SetScope(50);
        run.TransitionTo(RunState.Enriching, Now.AddHours(-3));
        run.TransitionTo(RunState.Matching, Now.AddHours(-3));
        run.TransitionTo(RunState.Ranking, Now.AddHours(-3));
        run.TransitionTo(RunState.Researching, Now.AddHours(-3));
        run.TransitionTo(RunState.Reporting, Now.AddHours(-3));
        run.TransitionTo(RunState.Delivered, Now.AddHours(-2));
        return run;
    }

    private static Digest AnyDigest() =>
        new(Guid.NewGuid(), RunId, DigestMode.Full, totalNewJobs: 3, strongMatches: 2, avgSalaryUsd: null,
            suppressedCount: 0, suppressionBreakdown: [], carriedOverCount: 0, companiesChecked: 0, analysedCount: 3,
            degradedSources: [], narrative: null, narrativeSource: NarrativeSource.Template, promptVersion: null,
            cards: [], generatedAt: Now.AddHours(-2));

    private static RenderableMessage Card(string key) =>
        new(RehydrateKey(key), RenderedMessage.PlainText(key));

    private static CardKey RehydrateKey(string key) => CardKey.TryCreate(key).Value!;

    private void RunResolves(Run? run) => _runs.FindActiveRunAsync(Arg.Any<CancellationToken>()).Returns(run);
    private void DigestResolves(Digest? digest) =>
        _digests.FindByRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(digest);
    private void Renders(params RenderableMessage[] messages) =>
        _renderer.RenderAsync(Arg.Any<Digest>(), Arg.Any<CancellationToken>()).Returns(messages);
    private void AlreadyDelivered(params string[] keys) =>
        _deliveryLog.DeliveredKeysAsync(RunId, OwnerChat, Arg.Any<CancellationToken>()).Returns(keys);

    [Fact]
    public async Task It_states_zero_when_every_card_was_already_delivered()
    {
        RunResolves(DeliveredRun());
        DigestResolves(AnyDigest());
        Renders(Card(CardKey.HeaderValue), Card("aaaaaaaaaaaaaaaa"), Card("bbbbbbbbbbbbbbbb"));
        AlreadyDelivered(CardKey.HeaderValue, "aaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb");

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("0");
    }

    [Fact]
    public async Task It_counts_only_the_cards_the_delivery_log_has_not_seen()
    {
        RunResolves(DeliveredRun());
        DigestResolves(AnyDigest());
        Renders(Card(CardKey.HeaderValue), Card("aaaaaaaaaaaaaaaa"), Card("bbbbbbbbbbbbbbbb"));
        AlreadyDelivered(CardKey.HeaderValue, "aaaaaaaaaaaaaaaa");

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        // Only bbbb… is unseen — one card would actually go out.
        text.ShouldContain("1");
    }

    [Fact]
    public async Task It_reads_the_delivery_log_for_the_requesting_chat()
    {
        RunResolves(DeliveredRun());
        DigestResolves(AnyDigest());
        Renders(Card(CardKey.HeaderValue));
        AlreadyDelivered();

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        await _deliveryLog.Received(1).DeliveredKeysAsync(RunId, OwnerChat, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_stores_a_pending_confirm_state_for_the_resume_step()
    {
        RunResolves(DeliveredRun());
        DigestResolves(AnyDigest());
        Renders(Card(CardKey.HeaderValue), Card("aaaaaaaaaaaaaaaa"));
        AlreadyDelivered(CardKey.HeaderValue);

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // Previews and asks; the write (publishing the redelivery) is deferred to the T10 confirm tap.
        await _state.Received(1).SetAsync(OwnerChat, Arg.Is<ConversationState>(s => s != null && s.Command == "redeliver"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_no_run_yet_it_says_so_and_stores_no_state()
    {
        RunResolves(null);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns((Run?)null);

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("No", Case.Insensitive);
        await _state.DidNotReceive().SetAsync(Arg.Any<long>(), Arg.Any<ConversationState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_a_run_but_no_assembled_digest_it_says_so_and_stores_no_state()
    {
        RunResolves(DeliveredRun());
        DigestResolves(null);

        var text = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null)))[0].Text;

        text.ShouldContain("No", Case.Insensitive);
        await _state.DidNotReceive().SetAsync(Arg.Any<long>(), Arg.Any<ConversationState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_falls_back_to_the_most_recent_run_when_none_is_live()
    {
        RunResolves(null);
        _runs.FindMostRecentRunAsync(Arg.Any<CancellationToken>()).Returns(DeliveredRun());
        DigestResolves(AnyDigest());
        Renders(Card(CardKey.HeaderValue));
        AlreadyDelivered(CardKey.HeaderValue);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages[0].Text.ShouldContain("0");
        await _runs.Received(1).FindMostRecentRunAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_confirm_reply_enqueues_the_redelivery_and_clears_the_state()
    {
        // The confirm tap re-runs delivery the only way a bus-less host can: it enqueues the same
        // DigestDeliveryTrigger the 07:00 slot fires, which the Worker's server runs and which publishes
        // DigestDeliveryDue. The delivery log makes it idempotent, so an already-sent card is never re-sent.
        var messages = await NewHandler().ResumeAsync(ConfirmResume("confirm"));

        _scheduler.Received(1).EnqueueDigestDelivery();
        await _state.Received(1).ClearAsync(OwnerChat, Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem().Text.ShouldContain("deliver", Case.Insensitive);
    }

    [Fact]
    public async Task A_non_confirm_reply_enqueues_nothing_and_leaves_the_state_for_another_reply()
    {
        var messages = await NewHandler().ResumeAsync(ConfirmResume("later"));

        _scheduler.DidNotReceive().EnqueueDigestDelivery();
        await _state.DidNotReceive().ClearAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        messages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_null_resume_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().ResumeAsync(null!));
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() => new RedeliverCommandHandler(null!, _digests, _renderer, _deliveryLog, _state, _scheduler, _clock, NullLogger<RedeliverCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RedeliverCommandHandler(_runs, null!, _renderer, _deliveryLog, _state, _scheduler, _clock, NullLogger<RedeliverCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RedeliverCommandHandler(_runs, _digests, null!, _deliveryLog, _state, _scheduler, _clock, NullLogger<RedeliverCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RedeliverCommandHandler(_runs, _digests, _renderer, null!, _state, _scheduler, _clock, NullLogger<RedeliverCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RedeliverCommandHandler(_runs, _digests, _renderer, _deliveryLog, null!, _scheduler, _clock, NullLogger<RedeliverCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RedeliverCommandHandler(_runs, _digests, _renderer, _deliveryLog, _state, null!, _clock, NullLogger<RedeliverCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RedeliverCommandHandler(_runs, _digests, _renderer, _deliveryLog, _state, _scheduler, null!, NullLogger<RedeliverCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() => new RedeliverCommandHandler(_runs, _digests, _renderer, _deliveryLog, _state, _scheduler, _clock, null!));
    }
}
