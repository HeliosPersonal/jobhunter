using JobHunter.Domain.Postings;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the closure sweep (SAD §6.1, T13): the raw postings whose <c>last_seen_at</c> did
/// not advance into the most recent cycle — a posting last seen strictly before <paramref name="seenBefore"/>
/// is absent from its board and is a closure candidate. The T11 upsert bumps <c>last_seen_at</c> on every
/// unchanged re-fetch, so a posting still on the board is never a candidate (a <c>DO NOTHING</c> insert
/// would have broken this). Read-only (Dapper); defined in Domain so the handler depends on the port.
/// </summary>
public interface IClosureSweepQuery
{
    /// <param name="seenBefore">
    /// The cutoff: a live posting whose <c>last_seen_at</c> is strictly before this instant was not re-seen
    /// this cycle and is gone from its board. A posting that reappeared bumps its <c>last_seen_at</c> to at or
    /// after the cutoff and is excluded.
    /// </param>
    Task<IReadOnlyList<ClosedPosting>> ClosedSinceAsync(
        DateTimeOffset seenBefore,
        CancellationToken cancellationToken = default);
}
