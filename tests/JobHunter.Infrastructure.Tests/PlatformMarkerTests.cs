using JobHunter.Infrastructure.Persistence.Reference;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests;

public sealed class PlatformMarkerTests
{
    private readonly SequentialIdGenerator _ids = new();
    private readonly FakeClock _clock = new();

    [Fact]
    public void A_marker_is_created_pending_with_its_label_and_timestamp()
    {
        var marker = new PlatformMarker(_ids.NewId(), "bootstrap", MarkerStatus.Pending, _clock.UtcNow);

        marker.Label.ShouldBe("bootstrap");
        marker.Status.ShouldBe(MarkerStatus.Pending);
        marker.RecordedAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public void A_blank_label_is_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            new PlatformMarker(_ids.NewId(), "  ", MarkerStatus.Pending, _clock.UtcNow));
    }

    [Fact]
    public void An_empty_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            new PlatformMarker(Guid.Empty, "bootstrap", MarkerStatus.Pending, _clock.UtcNow));
    }

    [Fact]
    public void Activate_moves_the_marker_to_active_and_stamps_the_time()
    {
        var marker = new PlatformMarker(_ids.NewId(), "bootstrap", MarkerStatus.Pending, _clock.UtcNow);
        var activatedAt = _clock.Advance(TimeSpan.FromMinutes(5));

        marker.Activate(activatedAt);

        marker.Status.ShouldBe(MarkerStatus.Active);
        marker.RecordedAt.ShouldBe(activatedAt);
    }
}
