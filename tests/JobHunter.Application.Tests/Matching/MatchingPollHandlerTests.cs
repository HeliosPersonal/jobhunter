using JobHunter.Application.Enrichment;
using JobHunter.Application.Matching;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Matching;

/// <summary>
/// T06: the matching batch poller (F4 SAD §6.1). The deep-tier twin of F3's <c>BatchPollHandler</c>: a
/// delayed job that re-enqueues itself, never a loop, so the backoff schedule is asserted against a
/// <see cref="FakeClock"/> with no real waiting (S5). The properties that carry it: a redelivered poll
/// <em>polls the same provider batch and never resubmits</em> (AC-05, proven with a throw-on-submit client);
/// an ended batch <em>hands off to result processing without advancing the Run</em>; and an incomplete
/// batch at the deadline or the 6 h cap <em>ships partial, advances to Ranking and carries its items over</em>
/// (AC-09). Repository and client are substituted, so these are zero-database unit tests.
/// </summary>
public sealed class MatchingPollHandlerTests
{
    private static readonly DateTimeOffset SubmittedAt = new(2026, 8, 3, 2, 5, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly FakeLlmBatchClient _client = new() { PollsBeforeEnd = 1000 };
    private readonly IJitter _jitter = Substitute.For<IJitter>();
    private readonly FakeClock _clock = new(SubmittedAt.AddMinutes(1));
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private static readonly TimeZoneInfo KyivLike =
        TimeZoneInfo.CreateCustomTimeZone("Kyiv-like", TimeSpan.FromHours(3), "Kyiv-like", "Kyiv-like");

    private readonly PollOptions _options = new() { TimeZone = KyivLike };

    public MatchingPollHandlerTests()
    {
        _jitter.Apply(Arg.Any<TimeSpan>()).Returns(ci => ci.Arg<TimeSpan>());
    }

    private MatchingPollHandler CreateHandler() =>
        new(_runs, _client, _jitter, _clock, _options, NullLogger<MatchingPollHandler>.Instance);

    private static Run MatchingRun()
    {
        var run = new Run(RunId, SubmittedAt.AddHours(-24), SubmittedAt, 2.00m, SubmittedAt.AddMinutes(-5));
        run.SetScope(2);
        run.TransitionTo(RunState.Enriching, SubmittedAt);
        run.TransitionTo(RunState.Matching, SubmittedAt);
        return run;
    }

    private Batch GivenBatch(int itemCount = 2)
    {
        var batch = new Batch(
            Guid.CreateVersion7(), RunId, BatchStage.Matching, ModelTier.Deep,
            "msgbatch_match_poll_01", "match-v1", itemCount, SubmittedAt);
        _runs.FindBatchAsync(RunId, BatchStage.Matching, ModelTier.Deep, Arg.Any<CancellationToken>())
            .Returns(batch);
        return batch;
    }

    private void GivenItems(Batch batch, int count)
    {
        var items = Enumerable.Range(0, count)
            .Select(_ => new BatchItem(Guid.CreateVersion7(), batch.Id, Guid.CreateVersion7().ToString(), Guid.CreateVersion7()))
            .ToList();
        _runs.FindBatchItemsAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(items);
    }

    private List<(object Message, DeliveryOptions? Options)> Publishes() =>
        _bus.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMessageBus.PublishAsync))
            .Select(c => c.GetArguments())
            .Where(a => a.Length > 0 && a[0] is not null)
            .Select(a => (a[0]!, a.Length > 1 ? a[1] as DeliveryOptions : null))
            .ToList();

    private List<TimeSpan?> PollDelays() =>
        Publishes().Where(p => p.Message is MatchingPollDue).Select(p => p.Options?.ScheduleDelay).ToList();

    // ---- backoff schedule (S5) -----------------------------------------------------------------

    [Fact]
    public async Task An_in_progress_poll_records_an_attempt_and_reschedules_with_the_backoff_delay()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();

        await CreateHandler().Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        batch.PollAttempts.ShouldBe(1);
        batch.State.ShouldBe(BatchState.InProgress);
        PollDelays().ShouldHaveSingleItem().ShouldBe(TimeSpan.FromMinutes(2));
        _client.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Successive_polls_follow_the_two_four_eight_fifteen_schedule()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenBatch();
        var handler = CreateHandler();

        for (var i = 0; i < 5; i++)
        {
            await handler.Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        PollDelays().ShouldBe(
        [
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(15),
        ]);
    }

    // ---- AC-05: a restart mid-poll resumes the same batch and never resubmits -------------------

    [Fact]
    public async Task A_redelivered_poll_polls_the_persisted_provider_batch_and_never_resubmits()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenBatch();
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        _client.StatusCallCount.ShouldBe(1);
    }

    // ---- Ended: hand off to result processing, do not advance the Run here ----------------------

    [Fact]
    public async Task An_ended_batch_hands_off_to_result_processing_without_advancing_the_run()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        _client.PollsBeforeEnd = 0;

        await CreateHandler().Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        Publishes().Select(p => p.Message).OfType<MatchingResultsReady>().ShouldHaveSingleItem();
        PollDelays().ShouldBeEmpty();
        batch.PollAttempts.ShouldBe(1);
        run.State.ShouldBe(RunState.Matching); // result processing advances the Run, not the poller
    }

    // ---- AC-09: incomplete at the deadline ships partial to Ranking and carries items over -------

    [Fact]
    public async Task An_incomplete_batch_past_the_delivery_deadline_ships_partial_and_carries_over()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch(itemCount: 2);
        GivenItems(batch, 2);
        _clock.Set(new DateTimeOffset(2026, 8, 3, 6, 50, 0, TimeSpan.FromHours(3)));

        await CreateHandler().Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        run.State.ShouldBe(RunState.Ranking);
        run.JobsCarriedOver.ShouldBe(2);
        batch.State.ShouldBe(BatchState.Failed);
        PollDelays().ShouldBeEmpty();
        Publishes().Select(p => p.Message).OfType<MatchingCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Carried_over_items_are_marked_retriable_so_the_next_run_re_scopes_them()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch(itemCount: 3);
        var items = Enumerable.Range(0, 3)
            .Select(_ => new BatchItem(Guid.CreateVersion7(), batch.Id, Guid.CreateVersion7().ToString(), Guid.CreateVersion7()))
            .ToList();
        _runs.FindBatchItemsAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(items);
        _clock.Set(new DateTimeOffset(2026, 8, 3, 6, 50, 0, TimeSpan.FromHours(3)));

        await CreateHandler().Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        items.ShouldAllBe(i => i.State == BatchItemState.ProviderError);
    }

    // ---- the 6 h cap ---------------------------------------------------------------------------

    [Fact]
    public async Task An_incomplete_batch_past_the_six_hour_cap_fails_and_carries_over()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch(itemCount: 2);
        GivenItems(batch, 2);
        var options = new PollOptions { DeliveryDeadlineLocalTime = null, MaxPollDuration = TimeSpan.FromHours(6) };
        _clock.Set(SubmittedAt.AddHours(6).AddMinutes(1));
        var handler = new MatchingPollHandler(_runs, _client, _jitter, _clock, options, NullLogger<MatchingPollHandler>.Instance);

        await handler.Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        batch.State.ShouldBe(BatchState.Failed);
        run.State.ShouldBe(RunState.Ranking);
        run.JobsCarriedOver.ShouldBe(2);
        PollDelays().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_batch_within_the_cap_and_before_the_deadline_keeps_polling()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenBatch();
        var options = new PollOptions { DeliveryDeadlineLocalTime = null, MaxPollDuration = TimeSpan.FromHours(6) };
        _clock.Set(SubmittedAt.AddHours(1));
        var handler = new MatchingPollHandler(_runs, _client, _jitter, _clock, options, NullLogger<MatchingPollHandler>.Instance);

        await handler.Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        PollDelays().ShouldHaveSingleItem();
        run.State.ShouldBe(RunState.Matching);
    }

    // ---- provider expiry and guards ------------------------------------------------------------

    [Fact]
    public async Task A_provider_expired_batch_is_carried_over_rather_than_polled_forever()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch(itemCount: 2);
        GivenItems(batch, 2);
        var expiredClient = new FakeLlmBatchClient(terminalState: ProviderBatchState.Expired) { PollsBeforeEnd = 0 };
        var handler = new MatchingPollHandler(_runs, expiredClient, _jitter, _clock, _options, NullLogger<MatchingPollHandler>.Instance);

        await handler.Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        batch.State.ShouldBe(BatchState.Expired);
        run.State.ShouldBe(RunState.Ranking);
        run.JobsCarriedOver.ShouldBe(2);
        PollDelays().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_already_terminal_batch_is_not_polled_again()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        batch.TransitionTo(BatchState.Completed, SubmittedAt.AddMinutes(10));

        await CreateHandler().Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        _client.StatusCallCount.ShouldBe(0);
        PollDelays().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_with_no_batch_is_a_no_op()
    {
        var run = MatchingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _runs.FindBatchAsync(RunId, BatchStage.Matching, ModelTier.Deep, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);

        await CreateHandler().Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        _client.StatusCallCount.ShouldBe(0);
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_run_is_ignored()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns((Run?)null);

        await CreateHandler().Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        _client.StatusCallCount.ShouldBe(0);
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_terminal_run_is_ignored()
    {
        var run = MatchingRun();
        run.Abort("done", SubmittedAt.AddHours(1), costBreach: false);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);

        await CreateHandler().Handle(new MatchingPollDue(RunId), _bus, CancellationToken.None);

        _client.StatusCallCount.ShouldBe(0);
    }
}
