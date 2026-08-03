using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Pipeline;

public sealed class BatchTests
{
    private static readonly Guid BatchId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    private static Batch NewBatch(FakeClock? clock = null) =>
        new(
            BatchId,
            RunId,
            BatchStage.Enrichment,
            ModelTier.Cheap,
            "msgbatch_123",
            "enrich-v1",
            10,
            (clock ?? new FakeClock()).UtcNow);

    [Fact]
    public void New_batch_is_submitted_with_the_provider_anchor_persisted()
    {
        var clock = new FakeClock();

        var batch = NewBatch(clock);

        batch.State.ShouldBe(BatchState.Submitted);
        batch.ProviderBatchId.ShouldBe("msgbatch_123");
        batch.PromptVersion.ShouldBe("enrich-v1");
        batch.ItemCount.ShouldBe(10);
        batch.Stage.ShouldBe(BatchStage.Enrichment);
        batch.Tier.ShouldBe(ModelTier.Cheap);
        batch.PollAttempts.ShouldBe(0);
        batch.CompletedAt.ShouldBeNull();
        batch.InputTokens.ShouldBeNull();
        batch.OutputTokens.ShouldBeNull();
        batch.SubmittedAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Constructor_rejects_a_blank_provider_id_or_prompt_version()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentException>(() =>
            new Batch(BatchId, RunId, BatchStage.Enrichment, ModelTier.Cheap, " ", "enrich-v1", 1, clock.UtcNow));
        Should.Throw<ArgumentException>(() =>
            new Batch(BatchId, RunId, BatchStage.Enrichment, ModelTier.Cheap, "id", " ", 1, clock.UtcNow));
    }

    [Fact]
    public void Constructor_rejects_a_non_positive_item_count_or_empty_run()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new Batch(BatchId, RunId, BatchStage.Enrichment, ModelTier.Cheap, "id", "v", 0, clock.UtcNow));
        Should.Throw<ArgumentException>(() =>
            new Batch(BatchId, Guid.Empty, BatchStage.Enrichment, ModelTier.Cheap, "id", "v", 1, clock.UtcNow));
    }

    [Fact]
    public void RecordPoll_only_ever_counts_up()
    {
        var batch = NewBatch();

        batch.RecordPoll();
        batch.RecordPoll();

        batch.PollAttempts.ShouldBe(2);
    }

    [Fact]
    public void Completing_stamps_completed_at_and_the_token_counts()
    {
        var clock = new FakeClock();
        var batch = NewBatch(clock);
        batch.TransitionTo(BatchState.InProgress, clock.UtcNow);

        clock.Advance(TimeSpan.FromMinutes(30));
        var result = batch.TransitionTo(BatchState.Completed, clock.UtcNow, inputTokens: 5000, outputTokens: 800);

        result.IsSuccess.ShouldBeTrue();
        batch.State.ShouldBe(BatchState.Completed);
        batch.CompletedAt.ShouldBe(clock.UtcNow);
        batch.InputTokens.ShouldBe(5000);
        batch.OutputTokens.ShouldBe(800);
    }

    [Theory]
    [InlineData(BatchState.Submitted, BatchState.InProgress, true)]
    [InlineData(BatchState.Submitted, BatchState.Completed, true)]
    [InlineData(BatchState.Submitted, BatchState.Failed, true)]
    [InlineData(BatchState.Submitted, BatchState.Expired, true)]
    [InlineData(BatchState.InProgress, BatchState.Completed, true)]
    [InlineData(BatchState.InProgress, BatchState.Failed, true)]
    [InlineData(BatchState.InProgress, BatchState.Expired, true)]
    [InlineData(BatchState.Submitted, BatchState.Submitted, false)]
    [InlineData(BatchState.Completed, BatchState.InProgress, false)]
    [InlineData(BatchState.Failed, BatchState.Completed, false)]
    [InlineData(BatchState.Expired, BatchState.InProgress, false)]
    public void Transition_legality_matches_the_batch_state_table(BatchState from, BatchState to, bool legal)
    {
        var clock = new FakeClock();
        var batch = NewBatch(clock);
        if (from == BatchState.InProgress)
        {
            batch.TransitionTo(BatchState.InProgress, clock.UtcNow);
        }
        else if (from != BatchState.Submitted)
        {
            // Drive to a terminal from-state to exercise its (absent) outgoing edges.
            batch.TransitionTo(from, clock.UtcNow);
        }

        var result = batch.TransitionTo(to, clock.UtcNow);

        result.IsSuccess.ShouldBe(legal, $"transition {from} -> {to}");
        if (!legal)
        {
            result.Error.Code.ShouldBe(Batch.IllegalStateTransition.Code);
        }
    }

    [Fact]
    public void An_illegal_transition_names_the_attempted_pair()
    {
        var clock = new FakeClock();
        var batch = NewBatch(clock);
        batch.TransitionTo(BatchState.Completed, clock.UtcNow);

        var result = batch.TransitionTo(BatchState.InProgress, clock.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("Completed");
        result.Error.Message.ShouldContain("InProgress");
    }
}
