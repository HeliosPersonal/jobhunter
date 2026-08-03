namespace JobHunter.Application.Discovery;

/// <summary>
/// The six-hourly tick that opens a discovery cycle (SAD §6.1). Enqueued by Hangfire and handled by
/// <see cref="DiscoveryCycleHandler"/>, which fans it out into one <c>SourceFetchRequested</c> per due
/// source. It is an internal application message, not a cross-boundary integration event, so it lives in
/// the Application layer rather than in <c>Contracts</c>. <see cref="WindowStart"/> is stamped once when
/// the tick fires and reused for every fan-out message, so a cycle that runs twice for the same window
/// produces the same idempotency keys and the inbox deduplicates it (invariant 8).
/// </summary>
public sealed record DiscoveryCycleDue(DateTimeOffset WindowStart);
