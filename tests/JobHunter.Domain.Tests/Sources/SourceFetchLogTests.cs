using JobHunter.Domain.Sources;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Sources;

public sealed class SourceFetchLogTests
{
    private static readonly Guid LogId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");
    private static readonly Guid SourceId = Guid.Parse("00000000-0000-0000-0000-0000000000D2");
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 2, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Records_a_successful_attempt()
    {
        var log = new SourceFetchLog(LogId, SourceId, StartedAt, 120, 200, 40, 3, FetchOutcome.Success, "ok");

        log.SourceId.ShouldBe(SourceId);
        log.DurationMs.ShouldBe(120);
        log.HttpStatus.ShouldBe((short)200);
        log.PostingsReturned.ShouldBe(40);
        log.PostingsChanged.ShouldBe(3);
        log.Outcome.ShouldBe(FetchOutcome.Success);
        log.Detail.ShouldBe("ok");
    }

    [Fact]
    public void Transport_failure_uses_status_zero_and_null_detail()
    {
        var log = new SourceFetchLog(LogId, SourceId, StartedAt, 5000, 0, 0, 0, FetchOutcome.TransportError);

        log.HttpStatus.ShouldBe((short)0);
        log.Outcome.ShouldBe(FetchOutcome.TransportError);
        log.Detail.ShouldBeNull();
    }

    [Fact]
    public void Constructor_rejects_an_empty_source_id()
    {
        Should.Throw<ArgumentException>(() =>
            new SourceFetchLog(LogId, Guid.Empty, StartedAt, 1, 200, 0, 0, FetchOutcome.Success));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Constructor_rejects_negative_counts(int durationMs, int returned, int changed)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SourceFetchLog(LogId, SourceId, StartedAt, durationMs, 200, returned, changed, FetchOutcome.Success));
    }
}
