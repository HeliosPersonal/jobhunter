using JobHunter.Domain.Reporting;

namespace JobHunter.Application.Reporting;

/// <summary>
/// Groups near-duplicate candidates into one presented card at digest assembly (F5-T13, SAD §6.1). Two
/// candidates that are the <em>same real opening</em> — the same company and the same normalised title —
/// posted under a slightly different title or on a second board collapse to one representative, so the digest
/// never shows the Owner the same role twice. This is a <strong>presentation</strong> concern computed here,
/// not a canonicalisation concern: F2 owns canonical <c>Job</c> identity, F5 owns how near-duplicates are
/// <em>shown</em> (ADR-F2-0001, "computed at digest assembly"). It never re-opens the dedup pipeline — it reads
/// only the already-assembled candidates.
///
/// <para>The rule is conservative by design (ADR-F2-0001, the F2 "zero false merges" floor realised at display
/// time): grouping needs both a non-empty company and a non-blank normalised title, so a missing key is a
/// distinct card, not a false merge that would hide a real role. When in doubt, do not group.</para>
///
/// <para>It is pure and deterministic: the input is the ordered, score-descending selected set, so the first
/// member of each group is its highest-scored candidate and becomes the representative, ties already broken by
/// the query's <c>final_score DESC, job_id</c> order. The same set always groups the same way and picks the
/// same representative, so a replay reproduces the grouping the persisted digest snapshotted.</para>
/// </summary>
public static class NearDuplicateGrouper
{
    /// <summary>
    /// Collapses <paramref name="selected"/> — already ordered best-first — into representatives, each carrying
    /// the job ids it grouped away. A candidate with no company or a blank normalised title is its own
    /// representative and groups nothing. Representatives keep the input order.
    /// </summary>
    public static IReadOnlyList<DigestCardGroup> Group(IReadOnlyList<DigestCandidate> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        var groups = new List<Mutable>(selected.Count);
        var byKey = new Dictionary<(Guid Company, string Title), Mutable>();

        foreach (var candidate in selected)
        {
            var key = KeyOf(candidate);
            if (key is null)
            {
                // Not groupable (no company or blank title): a distinct card that stands alone.
                groups.Add(new Mutable(candidate));
                continue;
            }

            if (byKey.TryGetValue(key.Value, out var representative))
            {
                // A later, lower-scored duplicate of an already-seen opening: fold it into the representative.
                representative.GroupedJobIds.Add(candidate.JobId);
            }
            else
            {
                var group = new Mutable(candidate);
                byKey.Add(key.Value, group);
                groups.Add(group);
            }
        }

        return groups
            .Select(g => new DigestCardGroup(g.Representative, g.GroupedJobIds))
            .ToList();
    }

    // The conservative fingerprint for display grouping: (company, normalised title), invariant-lowered and
    // trimmed. Null when either half is missing, so a candidate with no key never groups (ADR-F2-0001).
    private static (Guid Company, string Title)? KeyOf(DigestCandidate candidate)
    {
        if (candidate.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(candidate.NormalisedTitle))
        {
            return null;
        }

        return (candidate.CompanyId, candidate.NormalisedTitle.Trim().ToLowerInvariant());
    }

    private sealed class Mutable(DigestCandidate representative)
    {
        public DigestCandidate Representative { get; } = representative;

        public List<Guid> GroupedJobIds { get; } = [];
    }
}

/// <summary>
/// One presented card's near-duplicate group (F5-T13): the representative candidate — the highest-scored of the
/// group — and the job ids that were grouped away onto it. <see cref="GroupedJobIds"/> is empty for a card that
/// stands alone. The grouped-away jobs remain queryable through this set; they are grouped, never dropped.
/// </summary>
public sealed record DigestCardGroup(DigestCandidate Representative, IReadOnlyList<Guid> GroupedJobIds);
