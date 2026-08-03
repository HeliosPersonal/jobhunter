using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the job-liveness check (SAD §6.2, T08): the live jobs whose <em>every</em> alias
/// has gone stale — the latest <c>job_aliases.last_seen_at</c> across the job is strictly before
/// <paramref name="seenBefore"/> — so the opening no longer appears on any board that carried it. A job
/// with even one fresh alias is live and is not returned (data-model §job_aliases — closed "when every
/// alias has gone stale, not when one has").
///
/// <para>Closure is suspended for a job any of whose contributing sources is still quarantined
/// (data-model §D4): a provider outage stops a source being fetched, which would make its jobs look stale,
/// so the query excludes any job with a quarantined source at the cutoff. Read-only (Dapper); defined in
/// Domain so the handler depends on the port, not the Infrastructure query.</para>
/// </summary>
public interface IStaleJobsQuery
{
    /// <param name="seenBefore">
    /// The two-cycle staleness cutoff: a live job whose latest alias sighting is strictly before this instant
    /// was absent from every board for the whole window and is a closure candidate.
    /// </param>
    /// <param name="quarantinedAsOf">
    /// The instant at which source quarantine is judged; a job whose source is inside its quarantine window at
    /// this instant is excluded so a provider outage never closes its jobs (§D4).
    /// </param>
    Task<IReadOnlyList<StaleJob>> StaleSinceAsync(
        DateTimeOffset seenBefore,
        DateTimeOffset quarantinedAsOf,
        CancellationToken cancellationToken = default);
}
