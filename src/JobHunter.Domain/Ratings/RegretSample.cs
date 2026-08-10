using JobHunter.Domain.Common;

namespace JobHunter.Domain.Ratings;

/// <summary>
/// One opened weekly regret sample (F4 T21, ADR-F4-0003) — the append-only marker that the pre-match filter's
/// falsification control has already run for a given week. Its whole purpose is the unique <c>week_start</c>
/// constraint: a redelivered or re-scheduled weekly tick that tries to open a sample for a week already sampled
/// is rejected by the constraint, so the cheap-tier matching behind <c>jobhunter.matching.regret</c> is never
/// run twice and never double-spends.
///
/// <para>Like the <see cref="RatingRound"/> it mirrors, the table is append-only: there is no update and no
/// delete path, because clearing a sample would let a redelivery re-run and double-spend. Unlike the rating
/// round it is keyed by <c>week_start</c> alone — the sample serves the single Owner, not a chat. It carries
/// nothing about the Owner's CV.</para>
/// </summary>
public sealed class RegretSample : Entity
{
    public RegretSample(Guid id, DateTimeOffset weekStart, DateTimeOffset openedAt)
        : base(id)
    {
        WeekStart = weekStart;
        OpenedAt = openedAt;
    }

    private RegretSample()
    {
    }

    /// <summary>The start of the half-open week <c>[week_start, week_start + 7d)</c> this sample covers.</summary>
    public DateTimeOffset WeekStart { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }
}
