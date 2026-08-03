namespace JobHunter.Domain.Jobs;

/// <summary>
/// A live job whose every alias has gone stale (SAD §6.2, T08): no contributing raw posting was seen at or
/// after the liveness cutoff, so the opening is gone from every board that ever carried it. Flat by design —
/// the liveness check loads the aggregate by <see cref="JobId"/> and closes it at <see cref="LastSeenAt"/>,
/// the latest alias sighting, which becomes the closure's idempotency component (invariant 8). A job with
/// even one contributing source still in quarantine is deliberately excluded upstream (data-model §D4): a
/// provider outage must not close its jobs, so the query never reports them.
/// </summary>
public sealed record StaleJob(Guid JobId, DateTimeOffset LastSeenAt);
