namespace JobHunter.Application.Discovery;

/// <summary>
/// The tick that opens a closure sweep (SAD §6.1, T13). Enqueued by Hangfire on the discovery cadence and
/// handled by <see cref="ClosureSweepHandler"/>, which finds the raw postings whose <c>last_seen_at</c> did
/// not advance this cycle — gone from their board — and publishes one <c>JobClosed</c> per posting. It is an
/// internal application message, not a cross-boundary integration event, so it lives in the Application layer
/// rather than in <c>Contracts</c>. <see cref="WindowStart"/> is the cutoff: a posting last seen before it
/// was absent for the whole of the most recent cycle. Stamped once when the tick fires and reused, so a
/// sweep that runs twice for the same window reads the same set and produces the same closure keys.
/// </summary>
public sealed record ClosureSweepDue(DateTimeOffset WindowStart);
