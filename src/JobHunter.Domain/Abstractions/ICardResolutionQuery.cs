using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over "the cards a callback short id could resolve to" (F5 T10, contract §Callback
/// payloads, AC-09). A callback's <c>callback_data</c> carries only a signed short id, so the handler needs
/// the recent delivered cards to HMAC-match it against — this port supplies them as
/// <see cref="CardCandidate"/>s. Read-only (Dapper); defined in Domain so the Telegram handler depends on
/// the port, not the SQL.
///
/// <para>The window is a caller-supplied cutoff rather than a hidden limit: the Telegram layer owns "how
/// far back a tap may still resolve" through its <c>IClock</c>, so a stale id from before the window falls
/// out of scope and produces the plain "this role has closed" message, never a silent no-op. It selects
/// <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is not this
/// one.</para>
/// </summary>
public interface ICardResolutionQuery
{
    /// <summary>
    /// The cards of every digest generated at or after <paramref name="since"/>, each with its card key,
    /// job id and apply URL, so a callback short id can be HMAC-resolved among them.
    /// </summary>
    Task<IReadOnlyList<CardCandidate>> CandidatesSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
