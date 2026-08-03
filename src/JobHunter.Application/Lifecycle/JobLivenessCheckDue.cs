namespace JobHunter.Application.Lifecycle;

/// <summary>
/// The tick that opens a job-liveness check (SAD §6.2, T08). Enqueued by Hangfire on the daily cadence and
/// handled by <see cref="JobLifecycleHandler"/>, which closes every live job whose every alias has gone
/// stale — last seen before <see cref="SeenBefore"/>, the two-cycle cutoff. It is an internal application
/// message, not a cross-boundary integration event, so it lives in the Application layer rather than in
/// <c>Contracts</c> (mirroring <c>ClosureSweepDue</c>).
///
/// <para><see cref="SeenBefore"/> is the staleness cutoff, stamped once when the tick fires and reused: a
/// job whose latest alias sighting is strictly before it was absent from every board for the whole window.
/// A fixed cutoff (not "now") is what makes the sweep idempotent — the closure time it records is the job's
/// own last sighting, so a check that runs twice for the same window closes the same jobs at the same
/// instant and re-publishes nothing.</para>
/// </summary>
public sealed record JobLivenessCheckDue(DateTimeOffset SeenBefore);
