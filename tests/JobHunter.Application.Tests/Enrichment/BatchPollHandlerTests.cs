using JobHunter.Application.Enrichment;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Enrichment;

/// <summary>
/// T11: the batch poller (F3 SAD §6.2/§6.3). It is a delayed job that re-enqueues itself, never a loop —
/// so the whole backoff schedule is asserted against <see cref="FakeClock"/> with no real waiting (S5,
/// test-plan §NFR). The two properties that carry the feature: a redelivered poll <em>polls the same
/// provider batch and never resubmits</em> (AC-05, proven with a client whose <c>SubmitAsync</c> would
/// throw), and an incomplete batch at the deadline or the 6 h cap <em>ships partial and carries its items
/// over</em> (AC-09). The repository and client are substituted, so these are zero-database unit tests.
/// </summary>
public sealed class BatchPollHandlerTests
{
    private static readonly DateTimeOffset SubmittedAt = new(2026, 8, 3, 2, 5, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");

    private readonly IRunRepository _runs = Substitute.For<IRunRepository>();
    private readonly FakeLlmBatchClient _client = new() { PollsBeforeEnd = 1000 };
    private readonly IJitter _jitter = Substitute.For<IJitter>();
    private readonly FakeClock _clock = new(SubmittedAt.AddMinutes(1));
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    // A fixed UTC+3 zone so the delivery-deadline arithmetic is deterministic regardless of the host's
    // time-zone database (Kyiv is UTC+3 in August; the poller reads the deadline in this zone).
    private static readonly TimeZoneInfo KyivLike =
        TimeZoneInfo.CreateCustomTimeZone("Kyiv-like", TimeSpan.FromHours(3), "Kyiv-like", "Kyiv-like");

    private readonly PollOptions _options = new() { TimeZone = KyivLike };

    public BatchPollHandlerTests()
    {
        // The default jitter is the identity, so the base backoff schedule is asserted directly; the
        // "no lockstep" spread is a separate property of SystemJitter (see SystemJitterTests).
        _jitter.Apply(Arg.Any<TimeSpan>()).Returns(ci => ci.Arg<TimeSpan>());
    }

    private BatchPollHandler CreateHandler() =>
        new(_runs, _client, _jitter, _clock, _options, NullLogger<BatchPollHandler>.Instance);

    private static Run EnrichingRun()
    {
        var run = new Run(RunId, SubmittedAt.AddHours(-24), SubmittedAt, 2.00m, SubmittedAt.AddMinutes(-5));
        run.SetScope(2);
        run.TransitionTo(RunState.Enriching, SubmittedAt);
        return run;
    }

    private Batch GivenBatch(int itemCount = 2)
    {
        var batch = new Batch(
            Guid.CreateVersion7(), RunId, BatchStage.Enrichment, ModelTier.Cheap,
            "msgbatch_poll_01", "enrich-v1", itemCount, SubmittedAt);
        _runs.FindBatchAsync(RunId, BatchStage.Enrichment, ModelTier.Cheap, Arg.Any<CancellationToken>())
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
        Publishes().Where(p => p.Message is BatchPollDue).Select(p => p.Options?.ScheduleDelay).ToList();

    // ---- backoff schedule (S5, test-plan §NFR) --------------------------------------------------

    [Fact]
    public async Task An_in_progress_poll_records_an_attempt_and_reschedules_with_the_backoff_delay()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        batch.PollAttempts.ShouldBe(1);
        PollDelays().ShouldHaveSingleItem().ShouldBe(TimeSpan.FromMinutes(2));
        _client.SubmitCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Successive_polls_follow_the_two_four_eight_fifteen_schedule()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenBatch();
        var handler = CreateHandler();

        // Five polls of a batch that never ends: the gaps are 2, 4, 8, 15, 15 minutes (capped).
        for (var i = 0; i < 5; i++)
        {
            await handler.Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);
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

    [Fact]
    public async Task The_reschedule_delay_passes_through_the_jitter()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenBatch();
        // A jitter that adds a fixed 30 s, so the scheduled delay reflects it rather than the raw schedule.
        _jitter.Apply(Arg.Any<TimeSpan>()).Returns(ci => ci.Arg<TimeSpan>() + TimeSpan.FromSeconds(30));

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        _jitter.Received(1).Apply(TimeSpan.FromMinutes(2));
        PollDelays().ShouldHaveSingleItem().ShouldBe(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_first_in_progress_poll_moves_the_batch_to_in_progress()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        batch.State.ShouldBe(BatchState.InProgress);
    }

    // ---- AC-05: a restart mid-poll resumes the same batch and never resubmits -------------------

    [Fact]
    public async Task A_redelivered_poll_polls_the_persisted_provider_batch_and_never_resubmits()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenBatch();
        // The tripwire: any submission during a poll is a double charge (AC-05, QG-1).
        _client.ThrowOnSubmit = true;

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        _client.SubmitCallCount.ShouldBe(0);
        _client.StatusCallCount.ShouldBe(1);
    }

    // ---- Ended: hand off to result processing, do not advance the Run here ----------------------

    [Fact]
    public async Task An_ended_batch_hands_off_to_result_processing_without_advancing_the_run()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        _client.PollsBeforeEnd = 0; // ended on the first poll

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        Publishes().Select(p => p.Message).OfType<BatchResultsReady>().ShouldHaveSingleItem();
        PollDelays().ShouldBeEmpty(); // no reschedule once ended
        batch.PollAttempts.ShouldBe(1);
        run.State.ShouldBe(RunState.Enriching); // T12 advances the Run, not the poller
    }

    // ---- AC-09: incomplete at the deadline ships partial and carries items over -----------------

    [Fact]
    public async Task An_incomplete_batch_past_the_delivery_deadline_ships_partial_and_carries_over()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch(itemCount: 2);
        GivenItems(batch, 2);
        // 06:50 local Kyiv is past the 06:45 delivery cut but well within the 6 h cap.
        _clock.Set(new DateTimeOffset(2026, 8, 3, 6, 50, 0, TimeSpan.FromHours(3)));

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        run.State.ShouldBe(RunState.Matching);
        run.JobsCarriedOver.ShouldBe(2);
        batch.State.ShouldBe(BatchState.Failed);
        PollDelays().ShouldBeEmpty(); // 07:00 is never delayed — no further poll is scheduled
        Publishes().Select(p => p.Message).OfType<EnrichmentCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Carried_over_items_are_marked_retriable_so_the_next_run_re_scopes_them()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch(itemCount: 3);
        var items = Enumerable.Range(0, 3)
            .Select(_ => new BatchItem(Guid.CreateVersion7(), batch.Id, Guid.CreateVersion7().ToString(), Guid.CreateVersion7()))
            .ToList();
        _runs.FindBatchItemsAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(items);
        _clock.Set(new DateTimeOffset(2026, 8, 3, 6, 50, 0, TimeSpan.FromHours(3)));

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        // A carried-over item is retriable next Run — the submit handler's FindRetriableJobIdsAsync looks
        // for exactly ProviderError/ParseFailed items below the retry ceiling.
        items.ShouldAllBe(i => i.State == BatchItemState.ProviderError);
    }

    // ---- the 6 h cap: fail the batch and carry its items to the next Run ------------------------

    [Fact]
    public async Task An_incomplete_batch_past_the_six_hour_cap_fails_and_carries_over()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch(itemCount: 2);
        GivenItems(batch, 2);
        // Isolate the cap from the daily deadline, then jump past 6 h since submission.
        var options = new PollOptions { DeliveryDeadlineLocalTime = null, MaxPollDuration = TimeSpan.FromHours(6) };
        _clock.Set(SubmittedAt.AddHours(6).AddMinutes(1));
        var handler = new BatchPollHandler(_runs, _client, _jitter, _clock, options, NullLogger<BatchPollHandler>.Instance);

        await handler.Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        batch.State.ShouldBe(BatchState.Failed);
        run.State.ShouldBe(RunState.Matching);
        run.JobsCarriedOver.ShouldBe(2);
        PollDelays().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_batch_within_the_cap_and_before_the_deadline_keeps_polling()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        GivenBatch();
        var options = new PollOptions { DeliveryDeadlineLocalTime = null, MaxPollDuration = TimeSpan.FromHours(6) };
        _clock.Set(SubmittedAt.AddHours(1));
        var handler = new BatchPollHandler(_runs, _client, _jitter, _clock, options, NullLogger<BatchPollHandler>.Instance);

        await handler.Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        PollDelays().ShouldHaveSingleItem();
        run.State.ShouldBe(RunState.Enriching);
    }

    // ---- idempotency and guards -----------------------------------------------------------------

    [Fact]
    public async Task A_provider_expired_batch_is_carried_over_rather_than_polled_forever()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch(itemCount: 2);
        GivenItems(batch, 2);
        var expiredClient = new FakeLlmBatchClient(terminalState: ProviderBatchState.Expired) { PollsBeforeEnd = 0 };
        var handler = new BatchPollHandler(_runs, expiredClient, _jitter, _clock, _options, NullLogger<BatchPollHandler>.Instance);

        await handler.Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        batch.State.ShouldBe(BatchState.Expired);
        run.JobsCarriedOver.ShouldBe(2);
        PollDelays().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_already_terminal_batch_is_not_polled_again()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        var batch = GivenBatch();
        batch.TransitionTo(BatchState.Completed, SubmittedAt.AddMinutes(10));

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        _client.StatusCallCount.ShouldBe(0);
        PollDelays().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_with_no_batch_is_a_no_op()
    {
        var run = EnrichingRun();
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _runs.FindBatchAsync(RunId, BatchStage.Enrichment, ModelTier.Cheap, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        _client.StatusCallCount.ShouldBe(0);
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_run_is_ignored()
    {
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns((Run?)null);

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        _client.StatusCallCount.ShouldBe(0);
        Publishes().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_terminal_run_is_ignored()
    {
        var run = EnrichingRun();
        run.Abort("done", SubmittedAt.AddHours(1), costBreach: false);
        _runs.FindAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);

        await CreateHandler().Handle(new BatchPollDue(RunId), _bus, CancellationToken.None);

        _client.StatusCallCount.ShouldBe(0);
    }
}
