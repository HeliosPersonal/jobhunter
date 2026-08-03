using JobHunter.Application.Discovery;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Discovery;

/// <summary>
/// T10: the discovery cycle fans a due-source set out into one <see cref="SourceFetchRequested"/> per
/// source (QG-1) and does so idempotently — a cycle re-run for the same window re-uses the same window
/// stamp, so the fan-out messages carry the same <c>(SourceId, WindowStart)</c> keys and the inbox
/// deduplicates the fetch. The due-source read is substituted, so these are zero-database unit tests.
/// </summary>
public sealed class DiscoveryCycleHandlerTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private readonly IDiscoveryCycleQuery _due = Substitute.For<IDiscoveryCycleQuery>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly FakeClock _clock = new(WindowStart.AddSeconds(1));
    private readonly DiscoveryOptions _options = new();

    private DiscoveryCycleHandler CreateHandler() =>
        new(_due, _clock, NullLogger<DiscoveryCycleHandler>.Instance);

    [Fact]
    public async Task It_publishes_exactly_one_fetch_request_per_due_source()
    {
        var a = new DueSource(Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Greenhouse));
        var b = new DueSource(Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Lever));
        _due.DueSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([a, b]);

        var published = new List<SourceFetchRequested>();
        await _bus.PublishAsync(Arg.Do<SourceFetchRequested>(m => published.Add(m)));

        await CreateHandler().Handle(new DiscoveryCycleDue(WindowStart), _bus, _options, CancellationToken.None);

        published.Count.ShouldBe(2);
        published.Select(m => m.SourceId).ShouldBe([a.SourceId, b.SourceId]);
        published.ShouldAllBe(m => m.WindowStart == WindowStart);
    }

    [Fact]
    public async Task It_carries_the_company_and_ats_kind_onto_each_message()
    {
        var source = new DueSource(Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Ashby));
        _due.DueSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([source]);

        SourceFetchRequested? captured = null;
        await _bus.PublishAsync(Arg.Do<SourceFetchRequested>(m => captured = m));

        await CreateHandler().Handle(new DiscoveryCycleDue(WindowStart), _bus, _options, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.CompanyId.ShouldBe(source.CompanyId);
        captured.AtsKind.ShouldBe(nameof(AtsKind.Ashby));
    }

    [Fact]
    public async Task An_empty_cycle_publishes_nothing()
    {
        _due.DueSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateHandler().Handle(new DiscoveryCycleDue(WindowStart), _bus, _options, CancellationToken.None);

        await _bus.DidNotReceive().PublishAsync(Arg.Any<SourceFetchRequested>());
    }

    [Fact]
    public async Task Two_runs_for_the_same_window_produce_the_same_idempotency_keys()
    {
        var source = new DueSource(Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Greenhouse));
        _due.DueSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([source]);

        var published = new List<SourceFetchRequested>();
        await _bus.PublishAsync(Arg.Do<SourceFetchRequested>(m => published.Add(m)));
        var handler = CreateHandler();
        var cycle = new DiscoveryCycleDue(WindowStart);

        await handler.Handle(cycle, _bus, _options, CancellationToken.None);
        await handler.Handle(cycle, _bus, _options, CancellationToken.None);

        // The key is (SourceId, WindowStart). Both runs carry the same window, so the inbox collapses them.
        published.Count.ShouldBe(2);
        published[0].SourceId.ShouldBe(published[1].SourceId);
        published[0].WindowStart.ShouldBe(published[1].WindowStart);
    }

    [Fact]
    public async Task The_recent_fetch_cutoff_is_the_window_start_minus_the_configured_window()
    {
        _due.DueSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateHandler().Handle(new DiscoveryCycleDue(WindowStart), _bus, _options, CancellationToken.None);

        await _due.Received(1).DueSourcesAsync(
            Arg.Any<DateTimeOffset>(),
            WindowStart - _options.RecentFetchWindow,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_fans_out_the_target_band_source_before_an_equivalent_lower_band_one()
    {
        // Two otherwise-identical due sources arriving in the "wrong" order: the lower-band one first.
        // The bias must fetch the Top / remote-from-EMEA-friendly source before the untagged one (T15).
        var lowerBand = new DueSource(
            Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Greenhouse));
        var targetBand = new DueSource(
            Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Ashby),
            CompBand: nameof(CompBand.Top), RemoteEmeaFriendly: true);
        _due.DueSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([lowerBand, targetBand]);

        var published = new List<SourceFetchRequested>();
        await _bus.PublishAsync(Arg.Do<SourceFetchRequested>(m => published.Add(m)));

        await CreateHandler().Handle(new DiscoveryCycleDue(WindowStart), _bus, _options, CancellationToken.None);

        // Both are still published — the bias re-orders, it never filters.
        published.Count.ShouldBe(2);
        published[0].SourceId.ShouldBe(targetBand.SourceId);
        published[1].SourceId.ShouldBe(lowerBand.SourceId);
    }

    [Fact]
    public async Task Untagged_sources_keep_fanning_out_in_their_original_order()
    {
        // No comp band, no remote flag on any source: the fan-out must be unchanged from the read order,
        // so an untagged registry (the pre-T15 state) has no behaviour regression.
        var first = new DueSource(Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Greenhouse));
        var second = new DueSource(Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Lever));
        var third = new DueSource(Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Ashby));
        _due.DueSourcesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([first, second, third]);

        var published = new List<SourceFetchRequested>();
        await _bus.PublishAsync(Arg.Do<SourceFetchRequested>(m => published.Add(m)));

        await CreateHandler().Handle(new DiscoveryCycleDue(WindowStart), _bus, _options, CancellationToken.None);

        published.Select(m => m.SourceId).ShouldBe([first.SourceId, second.SourceId, third.SourceId]);
    }
}
