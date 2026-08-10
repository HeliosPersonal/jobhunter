using JobHunter.Domain.Common;

namespace JobHunter.Domain.Ratings;

/// <summary>
/// One opened weekly rating round (F4 T20 done-when 5) — the append-only marker that the Owner has already been
/// prompted to rate a given week's top-ten delivered cards. Its whole purpose is the unique
/// <c>(week_start, chat_id)</c> constraint: a redelivered or re-scheduled weekly tick that tries to open a
/// round for a week already opened is rejected by the constraint, so the ratings — and therefore
/// <c>precision@10</c> — are never double-counted.
///
/// <para>Like the <c>delivery_log</c> it mirrors (invariant 8), the table is append-only: there is no update
/// and no delete path, because clearing a round would mean re-prompting the Owner for a week already rated. It
/// is kept distinct from the <c>delivery_log</c> so that opening a round never looks like a card delivery to
/// <c>/stats</c>.</para>
/// </summary>
public sealed class RatingRound : Entity
{
    public RatingRound(Guid id, DateTimeOffset weekStart, long chatId, DateTimeOffset openedAt)
        : base(id)
    {
        WeekStart = weekStart;
        ChatId = chatId;
        OpenedAt = openedAt;
    }

    private RatingRound()
    {
    }

    /// <summary>The start of the half-open week <c>[week_start, week_start + 7d)</c> this round rates.</summary>
    public DateTimeOffset WeekStart { get; private set; }

    /// <summary>The Owner's chat the round's prompts go to — the other half of the idempotence key.</summary>
    public long ChatId { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }
}
