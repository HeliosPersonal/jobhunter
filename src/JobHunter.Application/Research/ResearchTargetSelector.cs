using JobHunter.Domain.Research;

namespace JobHunter.Application.Research;

/// <summary>
/// Chooses which companies a research cycle acts on (SAD §6.1, AC-05, AC-06). From the candidates behind a
/// Run's top jobs it keeps those that have never been researched or whose latest dossier has gone stale,
/// takes the five highest-scoring, and returns any on-demand requests alongside — never in place of — the
/// automatic five. The selector is a pure function of its inputs and the passed-in clock reading, so the
/// ≤5 cap, the freshness boundaries and the no-double-queue rule are all deterministic under test; the clock
/// is read once at the edge (T08) and handed in.
/// </summary>
public static class ResearchTargetSelector
{
    /// <summary>The most automatic dossiers one cycle will produce (SAD §2, ≤ 5 per day).</summary>
    public const int MaxAutomaticTargets = 5;

    /// <summary>
    /// Selects the automatic and on-demand research targets. <paramref name="candidates"/> are the companies
    /// behind the day's top jobs with their scores and dossier freshness; <paramref name="onDemand"/> are the
    /// company ids the Owner has requested; <paramref name="now"/> is the current instant against which
    /// freshness is judged. On-demand requests never displace an automatic target, and a company already
    /// chosen automatically — or requested twice — is queued only once.
    /// </summary>
    public static ResearchTargets Select(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<Guid> onDemand,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(onDemand);

        var automatic = candidates
            .Where(c => NeedsResearch(c, now))
            .OrderByDescending(c => c.Score)
            .Take(MaxAutomaticTargets)
            .Select(c => c.CompanyId)
            .ToList();

        var automaticSet = automatic.ToHashSet();
        var seenOnDemand = new HashSet<Guid>();
        var onDemandTargets = new List<Guid>();
        foreach (var requested in onDemand)
        {
            // An on-demand request for a company already being researched this cycle, or repeated in the
            // same batch, adds nothing — it is acknowledged but queued once.
            if (!automaticSet.Contains(requested) && seenOnDemand.Add(requested))
            {
                onDemandTargets.Add(requested);
            }
        }

        return new ResearchTargets(automatic, onDemandTargets);
    }

    /// <summary>
    /// A candidate needs research if it has no dossier, or its dossier is stale. A dossier is stale as soon
    /// as it is stale for any category it covered — so a volatile category (news, layoffs) pulls the whole
    /// dossier's refresh forward. A dossier that covered nothing ages at the default window.
    /// </summary>
    private static bool NeedsResearch(ResearchCandidate candidate, DateTimeOffset now)
    {
        var dossier = candidate.LatestDossier;
        if (dossier is null)
        {
            return true;
        }

        // An empty dossier is judged by the non-volatile default window; any covered category can pull the
        // refresh forward, so the dossier is stale as soon as it is stale for the soonest-ageing one.
        var categories = dossier.CategoriesCovered.Count == 0
            ? [ResearchCategory.Funding]
            : dossier.CategoriesCovered;

        return categories.Any(category => Freshness.IsStale(dossier.GeneratedAt, category, now));
    }
}
