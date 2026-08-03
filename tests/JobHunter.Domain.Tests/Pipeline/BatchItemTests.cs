using JobHunter.Domain.Pipeline;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Pipeline;

public sealed class BatchItemTests
{
    private static readonly Guid ItemId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
    private static readonly Guid BatchId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");

    private static BatchItem NewItem() => new(ItemId, BatchId, JobId.ToString(), JobId);

    [Fact]
    public void New_item_is_pending_with_the_job_id_as_custom_id()
    {
        var item = NewItem();

        item.State.ShouldBe(BatchItemState.Pending);
        item.CustomId.ShouldBe(JobId.ToString());
        item.JobId.ShouldBe(JobId);
        item.RetryCount.ShouldBe(0);
        item.RawResult.ShouldBeNull();
        item.ParseError.ShouldBeNull();
    }

    [Fact]
    public void Constructor_rejects_a_blank_custom_id_or_empty_batch_or_job()
    {
        Should.Throw<ArgumentException>(() => new BatchItem(ItemId, BatchId, " ", JobId));
        Should.Throw<ArgumentException>(() => new BatchItem(ItemId, Guid.Empty, "cid", JobId));
        Should.Throw<ArgumentException>(() => new BatchItem(ItemId, BatchId, "cid", Guid.Empty));
    }

    [Fact]
    public void MarkParsed_clears_prior_failure_detail()
    {
        var item = NewItem();
        item.MarkParseFailed("bad json", "{raw}");

        item.MarkParsed();

        item.State.ShouldBe(BatchItemState.Parsed);
        item.RawResult.ShouldBeNull();
        item.ParseError.ShouldBeNull();
    }

    [Fact]
    public void MarkParseFailed_retains_the_error_and_raw_payload()
    {
        var item = NewItem();

        item.MarkParseFailed("schema invalid", "{\"x\":1}");

        item.State.ShouldBe(BatchItemState.ParseFailed);
        item.ParseError.ShouldBe("schema invalid");
        item.RawResult.ShouldBe("{\"x\":1}");
    }

    [Fact]
    public void MarkProviderError_records_a_retryable_provider_fault()
    {
        var item = NewItem();

        item.MarkProviderError("rate limited", null);

        item.State.ShouldBe(BatchItemState.ProviderError);
        item.ParseError.ShouldBe("rate limited");
    }

    [Fact]
    public void MarkParseFailed_and_MarkProviderError_reject_a_blank_error()
    {
        var item = NewItem();

        Should.Throw<ArgumentException>(() => item.MarkParseFailed(" ", null));
        Should.Throw<ArgumentException>(() => item.MarkProviderError(" ", null));
    }

    [Fact]
    public void First_retry_returns_the_item_to_pending()
    {
        var item = NewItem();
        item.MarkParseFailed("bad", "{raw}");

        var scheduled = item.TryScheduleRetry();

        scheduled.ShouldBeTrue();
        item.State.ShouldBe(BatchItemState.Pending);
        item.RetryCount.ShouldBe(1);
        item.RawResult.ShouldBeNull();
        item.ParseError.ShouldBeNull();
    }

    [Fact]
    public void A_second_failure_abandons_the_item_and_stops_retrying()
    {
        var item = NewItem();
        item.MarkParseFailed("first", "{raw}");
        item.TryScheduleRetry();
        item.MarkParseFailed("second", "{raw}");

        var scheduled = item.TryScheduleRetry();

        scheduled.ShouldBeFalse();
        item.State.ShouldBe(BatchItemState.Abandoned);
        item.RetryCount.ShouldBe(BatchItem.MaxRetries);
    }
}
