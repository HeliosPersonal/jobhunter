using JobHunter.Domain.Sources;

namespace JobHunter.Application.Discovery;

/// <summary>
/// Orders the due sources of one discovery cycle toward the Owner's target comp-and-remote band (T15,
/// TUNE-10). It is a bias, never a filter: every due source is returned, so coverage is unchanged and no
/// company is silently dropped — the segmentation only decides who is fetched first when a window's fan-out
/// is large. The order is a pure, stable function of the curated <see cref="DueSource.CompBand"/> and
/// <see cref="DueSource.RemoteEmeaFriendly"/> tags, so the same due-set always fans out in the same order
/// and an untagged company (both null) sorts after every tagged one rather than being penalised twice.
///
/// <para>Ordering is: higher comp band first (<c>Top</c> before <c>High</c> before <c>Mid</c> before
/// untagged), then remote-from-EMEA-friendly before not, then the original source order as the stable
/// tie-break. The reason for a source's rank (<see cref="PrioritizedSource.Reason"/>) is carried alongside
/// so a "why was this fetched first" question is answerable (invariant 4 spirit).</para>
/// </summary>
public static class DiscoveryPrioritizer
{
    /// <summary>
    /// Returns <paramref name="due"/> ordered toward the target band, each row carrying the reason for its
    /// rank. Input order is preserved as the stable tie-break, so two equally-banded sources keep their
    /// relative order and the fan-out is deterministic.
    /// </summary>
    public static IReadOnlyList<PrioritizedSource> Prioritize(IReadOnlyList<DueSource> due)
    {
        ArgumentNullException.ThrowIfNull(due);

        return due
            .Select((source, index) => (source, index))
            .OrderBy(x => CompBandRank(x.source.CompBand))
            .ThenBy(x => x.source.RemoteEmeaFriendly == true ? 0 : 1)
            .ThenBy(x => x.index)
            .Select(x => new PrioritizedSource(x.source, Reason(x.source)))
            .ToList();
    }

    // Lower rank sorts first. Top is the bullseye; an untagged company sorts last so a tagged one is always
    // preferred, but it is still fetched (bias, not filter).
    private static int CompBandRank(string? compBand) => compBand switch
    {
        nameof(Domain.Companies.CompBand.Top) => 0,
        nameof(Domain.Companies.CompBand.High) => 1,
        nameof(Domain.Companies.CompBand.Mid) => 2,
        _ => 3,
    };

    private static string Reason(DueSource source)
    {
        var band = source.CompBand is { } b ? $"comp band {b}" : "untagged comp band";
        var remote = source.RemoteEmeaFriendly switch
        {
            true => "remote-from-EMEA friendly",
            false => "not remote-from-EMEA friendly",
            null => "unknown remote posture",
        };

        return $"Prioritised by {band}, {remote}.";
    }
}

/// <summary>
/// A due source paired with the human-readable reason for its fan-out rank (T15). The reason makes the
/// comp-and-remote bias explainable rather than a silent re-order.
/// </summary>
public sealed record PrioritizedSource(DueSource Source, string Reason);
