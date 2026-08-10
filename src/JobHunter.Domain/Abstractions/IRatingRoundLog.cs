namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The per-week idempotence gate for the weekly rating loop (F4 T20 done-when 5). Opening a round for a week is
/// the "have I already prompted for this week?" check: <see cref="TryOpenAsync"/> inserts one row keyed by
/// <c>(week_start, chat_id)</c> and returns <c>true</c> only on a genuine insert, so a redelivered or
/// re-scheduled tick for a week already prompted opens nothing and sends nothing — the ratings, and therefore
/// <c>precision@10</c>, are never double-counted.
///
/// <para>It is append-only, mirroring the <c>delivery_log</c> discipline (invariant 8): there is no update and
/// no delete path, because clearing a round would mean re-prompting the Owner for a week already rated. It is
/// deliberately separate from the <c>delivery_log</c> so that opening a rating round never looks like a card
/// delivery to <c>/stats</c>.</para>
/// </summary>
public interface IRatingRoundLog
{
    /// <summary>
    /// Opens the rating round for the week beginning <paramref name="weekStart"/> for <paramref name="chatId"/>,
    /// returning <c>true</c> on a genuine insert and <c>false</c> when the round already existed. The unique
    /// <c>(week_start, chat_id)</c> constraint arbitrates, so only the first tick for a week prompts.
    /// </summary>
    Task<bool> TryOpenAsync(
        DateTimeOffset weekStart, long chatId, DateTimeOffset openedAt, CancellationToken cancellationToken = default);
}
