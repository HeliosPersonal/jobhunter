using JobHunter.Application.Messaging;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests;

public sealed class DeadLetterContractTests
{
    [Fact]
    public void Moved_reports_the_replayed_outcome_and_count()
    {
        var result = ReplayResult.Moved(7);

        result.Outcome.ShouldBe(ReplayOutcome.Replayed);
        result.MovedCount.ShouldBe(7);
        result.Message.ShouldBeNull();
    }

    [Fact]
    public void UnknownQueue_names_the_offending_queue()
    {
        var result = ReplayResult.UnknownQueue("orders.dlq");

        result.Outcome.ShouldBe(ReplayOutcome.UnknownQueue);
        result.MovedCount.ShouldBe(0);
        result.Message.ShouldNotBeNull().ShouldContain("orders.dlq");
    }

    [Fact]
    public void Empty_reports_an_empty_queue_and_moves_nothing()
    {
        var result = ReplayResult.Empty("orders.dlq");

        result.Outcome.ShouldBe(ReplayOutcome.EmptyQueue);
        result.MovedCount.ShouldBe(0);
        result.Message.ShouldNotBeNull().ShouldContain("orders.dlq");
    }

    [Fact]
    public void DeadLetterSummary_carries_queue_source_and_depth()
    {
        var summary = new DeadLetterSummary("orders.dlq", "orders", 12);

        summary.DeadLetterQueue.ShouldBe("orders.dlq");
        summary.SourceQueue.ShouldBe("orders");
        summary.MessageCount.ShouldBe(12u);
    }
}
