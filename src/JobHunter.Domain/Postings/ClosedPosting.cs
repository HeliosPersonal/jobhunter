namespace JobHunter.Domain.Postings;

/// <summary>
/// A raw posting that was live but is now gone from its board (SAD §6.1, T13): its <c>last_seen_at</c> did
/// not advance in the most recent cycle, so it no longer appears on the board. Flat by design — the closure
/// sweep publishes one <c>JobClosed</c> per row and nothing more, so the read carries only the posting's id
/// and the instant it was last seen (which becomes the closure's idempotency component).
/// </summary>
public sealed record ClosedPosting(Guid RawPostingId, DateTimeOffset LastSeenAt);
