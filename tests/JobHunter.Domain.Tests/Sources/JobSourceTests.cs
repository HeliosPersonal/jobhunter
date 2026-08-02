using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Sources;

public sealed class JobSourceTests
{
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private static readonly Guid BindingId = Guid.Parse("00000000-0000-0000-0000-0000000000BB");
    private static readonly Guid SourceId = Guid.Parse("00000000-0000-0000-0000-0000000000CC");

    private static JobSource NewSource() =>
        new(SourceId, CompanyId, BindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs");

    [Fact]
    public void New_source_is_healthy()
    {
        var source = NewSource();

        source.ConsecutiveFailures.ShouldBe((short)0);
        source.QuarantinedUntil.ShouldBeNull();
        source.RequestsPerSecond.ShouldBe((short)1);
        source.IsQuarantined(new FakeClock()).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_a_non_positive_rate(short rate)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new JobSource(SourceId, CompanyId, BindingId, "https://x", rate));
    }

    [Fact]
    public void Constructor_rejects_empty_company_or_binding_id()
    {
        Should.Throw<ArgumentException>(() => new JobSource(SourceId, Guid.Empty, BindingId, "https://x"));
        Should.Throw<ArgumentException>(() => new JobSource(SourceId, CompanyId, Guid.Empty, "https://x"));
    }

    [Fact]
    public void First_failure_does_not_quarantine()
    {
        var clock = new FakeClock();
        var source = NewSource();

        var quarantined = source.RecordFailure(clock, TimeSpan.FromHours(6));

        quarantined.ShouldBeFalse();
        source.ConsecutiveFailures.ShouldBe((short)1);
        source.QuarantinedUntil.ShouldBeNull();
    }

    [Fact]
    public void Second_consecutive_failure_quarantines_and_signals_once()
    {
        var clock = new FakeClock();
        var source = NewSource();

        source.RecordFailure(clock, TimeSpan.FromHours(6));
        var quarantined = source.RecordFailure(clock, TimeSpan.FromHours(6));

        quarantined.ShouldBeTrue();
        source.QuarantinedUntil.ShouldBe(clock.UtcNow + TimeSpan.FromHours(6));
        source.IsQuarantined(clock).ShouldBeTrue();

        // A third failure stays quarantined but does not re-signal.
        source.RecordFailure(clock, TimeSpan.FromHours(6)).ShouldBeFalse();
    }

    [Fact]
    public void Success_clears_failures_and_quarantine()
    {
        var clock = new FakeClock();
        var source = NewSource();
        source.RecordFailure(clock, TimeSpan.FromHours(6));
        source.RecordFailure(clock, TimeSpan.FromHours(6));

        source.RecordSuccess(clock);

        source.ConsecutiveFailures.ShouldBe((short)0);
        source.QuarantinedUntil.ShouldBeNull();
        source.LastFetchedAt.ShouldBe(clock.UtcNow);
        source.IsQuarantined(clock).ShouldBeFalse();
    }

    [Fact]
    public void Quarantine_expires_once_the_window_passes()
    {
        var clock = new FakeClock();
        var source = NewSource();
        source.RecordFailure(clock, TimeSpan.FromHours(6));
        source.RecordFailure(clock, TimeSpan.FromHours(6));

        clock.Advance(TimeSpan.FromHours(7));

        source.IsQuarantined(clock).ShouldBeFalse();
    }
}
