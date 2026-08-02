using JobHunter.Domain.Abstractions;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests;

public sealed class ClockAndIdGeneratorTests
{
    [Fact]
    public void SystemClock_reports_utc()
    {
        var before = DateTimeOffset.UtcNow;

        var now = new SystemClock().UtcNow;

        now.Offset.ShouldBe(TimeSpan.Zero);
        now.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        now.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void UuidV7Generator_produces_unique_ids()
    {
        var generator = new UuidV7Generator();

        var ids = Enumerable.Range(0, 1_000).Select(_ => generator.NewId()).ToList();

        ids.Distinct().Count().ShouldBe(1_000);
        ids.ShouldNotContain(Guid.Empty);
    }

    [Fact]
    public void UuidV7Generator_ids_sort_in_creation_order()
    {
        var generator = new UuidV7Generator();

        // v7 embeds a millisecond timestamp; draws separated in time must be non-decreasing.
        var first = generator.NewId();
        Thread.Sleep(5);
        var second = generator.NewId();

        Comparer<Guid>.Default.Compare(second, first).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void FakeClock_defaults_to_a_fixed_instant_and_moves_only_when_told()
    {
        var clock = new FakeClock();

        clock.UtcNow.ShouldBe(FakeClock.DefaultNow);

        clock.Advance(TimeSpan.FromHours(2));
        clock.UtcNow.ShouldBe(FakeClock.DefaultNow.AddHours(2));

        var target = new DateTimeOffset(2030, 5, 1, 0, 0, 0, TimeSpan.Zero);
        clock.Set(target);
        clock.UtcNow.ShouldBe(target);
    }

    [Fact]
    public void FakeClock_can_be_constructed_at_an_arbitrary_instant()
    {
        var anchor = new DateTimeOffset(2027, 3, 3, 9, 0, 0, TimeSpan.Zero);

        new FakeClock(anchor).UtcNow.ShouldBe(anchor);
    }

    [Fact]
    public void SequentialIdGenerator_yields_ascending_ids_and_counts_them()
    {
        var generator = new SequentialIdGenerator();

        var a = generator.NewId();
        var b = generator.NewId();

        generator.Count.ShouldBe(2);
        Comparer<Guid>.Default.Compare(b, a).ShouldBeGreaterThan(0);
        a.ShouldNotBe(Guid.Empty);
    }
}
