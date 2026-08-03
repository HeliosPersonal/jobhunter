using JobHunter.Application.Discovery;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Discovery;

/// <summary>
/// T15 (TUNE-10): the fan-out is biased toward the Owner's target comp-and-remote band, but never filtered.
/// Every due source is returned; the order alone changes, higher comp band first, then remote-from-EMEA
/// friendly, then the original read order as a stable tie-break. Each row carries the reason for its rank so
/// "why was this fetched first" is answerable. Pure function — no clock, no database.
/// </summary>
public sealed class DiscoveryPrioritizerTests
{
    private static DueSource Source(string? compBand = null, bool? remoteEmea = null) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), nameof(AtsKind.Greenhouse), compBand, remoteEmea);

    [Fact]
    public void It_orders_higher_comp_band_first()
    {
        var mid = Source(nameof(CompBand.Mid));
        var top = Source(nameof(CompBand.Top));
        var high = Source(nameof(CompBand.High));

        var ordered = DiscoveryPrioritizer.Prioritize([mid, top, high]);

        ordered.Select(p => p.Source).ShouldBe([top, high, mid]);
    }

    [Fact]
    public void An_untagged_source_sorts_after_every_tagged_one_but_is_still_returned()
    {
        var untagged = Source();
        var mid = Source(nameof(CompBand.Mid));

        var ordered = DiscoveryPrioritizer.Prioritize([untagged, mid]);

        ordered.Select(p => p.Source).ShouldBe([mid, untagged]);
        ordered.Count.ShouldBe(2);
    }

    [Fact]
    public void Within_a_band_remote_from_emea_friendly_sorts_first()
    {
        var notRemote = Source(nameof(CompBand.High), remoteEmea: false);
        var remote = Source(nameof(CompBand.High), remoteEmea: true);

        var ordered = DiscoveryPrioritizer.Prioritize([notRemote, remote]);

        ordered.Select(p => p.Source).ShouldBe([remote, notRemote]);
    }

    [Fact]
    public void Equal_sources_keep_their_original_order_as_a_stable_tie_break()
    {
        var a = Source(nameof(CompBand.High), remoteEmea: true);
        var b = Source(nameof(CompBand.High), remoteEmea: true);
        var c = Source(nameof(CompBand.High), remoteEmea: true);

        var ordered = DiscoveryPrioritizer.Prioritize([a, b, c]);

        ordered.Select(p => p.Source).ShouldBe([a, b, c]);
    }

    [Fact]
    public void Comp_band_dominates_the_remote_flag()
    {
        // A Top band that is not remote-friendly still outranks a High band that is — band is the primary key.
        var topNotRemote = Source(nameof(CompBand.Top), remoteEmea: false);
        var highRemote = Source(nameof(CompBand.High), remoteEmea: true);

        var ordered = DiscoveryPrioritizer.Prioritize([highRemote, topNotRemote]);

        ordered.Select(p => p.Source).ShouldBe([topNotRemote, highRemote]);
    }

    [Fact]
    public void Every_row_carries_a_reason_naming_its_band_and_remote_posture()
    {
        var ordered = DiscoveryPrioritizer.Prioritize(
        [
            Source(nameof(CompBand.Top), remoteEmea: true),
            Source(remoteEmea: false),
            Source(),
        ]);

        ordered.ShouldAllBe(p => !string.IsNullOrWhiteSpace(p.Reason));
        ordered[0].Reason.ShouldContain("Top");
        ordered[0].Reason.ShouldContain("remote-from-EMEA friendly");
        ordered[^1].Reason.ShouldContain("untagged");
        ordered[^1].Reason.ShouldContain("unknown remote posture");
        ordered.ShouldContain(p => p.Reason.Contains("not remote-from-EMEA friendly"));
    }

    [Fact]
    public void An_empty_due_set_returns_empty()
    {
        DiscoveryPrioritizer.Prioritize([]).ShouldBeEmpty();
    }

    [Fact]
    public void It_rejects_a_null_argument()
    {
        Should.Throw<ArgumentNullException>(() => DiscoveryPrioritizer.Prioritize(null!));
    }
}
