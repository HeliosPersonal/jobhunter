using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Lifecycle;

/// <summary>
/// The daily job-liveness check (SAD §6.2, T08): a live job whose every alias has gone stale for two cycles
/// is closed, and exactly one <see cref="JobClosed"/> is published per closure. It is the mirror of F1's
/// closure sweep at the canonical-job level — F1 closes a posting gone from its board; this closes a job
/// gone from every board that carried it (data-model §job_aliases — "closed when every alias has gone
/// stale, not when one has").
///
/// <para>Reopening is not here: a reappearing posting hits the same fingerprint in the deduplication
/// handler, which reopens the very same job rather than forking a second one (AC-07) — the fingerprint
/// makes reopening automatic, so the lifecycle service never has to hunt for jobs to revive.</para>
///
/// <para>Closure is suspended for a job whose source is quarantined (data-model §D4): the query excludes
/// those candidates so a provider outage never closes its jobs, and the aggregate's own
/// <see cref="Domain.Jobs.Job.Close"/> refuses a quarantined job as a second net. <c>ClosedAt</c> is the
/// job's own latest alias sighting — a fixed instant, not "now" — so a check that runs twice for the same
/// window closes the same jobs at the same instant and the inbox collapses the duplicate <c>JobClosed</c>
/// (invariant 8). Idempotent on the job: a closed job re-read is a no-op that publishes nothing again.</para>
/// </summary>
public sealed class JobLifecycleHandler(
    IStaleJobsQuery staleJobs,
    IJobRepository jobs,
    IClock clock,
    ILogger<JobLifecycleHandler> logger)
{
    private readonly IStaleJobsQuery _staleJobs = staleJobs ?? throw new ArgumentNullException(nameof(staleJobs));
    private readonly IJobRepository _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<JobLifecycleHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>The reason recorded on every liveness-driven closure (event-catalog §3).</summary>
    public const string StaleAcrossAllSources = "StaleAcrossAllSources";

    public async Task Handle(JobLivenessCheckDue message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var candidates = await _staleJobs
            .StaleSinceAsync(message.SeenBefore, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        var closedCount = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = await _jobs.FindAsync(candidate.JobId, cancellationToken).ConfigureAwait(false);
            if (job is null)
            {
                _logger.LogWarning(
                    "Stale job {JobId} vanished before the liveness check could close it; skipping.",
                    candidate.JobId);
                continue;
            }

            // ClosedAt is the job's own last alias sighting, so a re-run of the same window closes at the same
            // instant and the (JobId, ClosedAt) key deduplicates the JobClosed downstream (invariant 8).
            var result = job.Close(candidate.LastSeenAt);
            if (result.IsFailure)
            {
                // A quarantined job cannot be closed — the query should already exclude it (§D4); this is the
                // second net, recorded rather than thrown so one odd job never halts the sweep.
                _logger.LogInformation(
                    "Job {JobId} was not closed by the liveness check: {Reason}.",
                    candidate.JobId, result.Error.Code);
                continue;
            }

            await bus.PublishAsync(new JobClosed(
                job.Id, candidate.LastSeenAt, StaleAcrossAllSources, _clock.UtcNow)).ConfigureAwait(false);
            closedCount++;
        }

        if (closedCount > 0)
        {
            await _jobs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Liveness check for cutoff {SeenBefore:o} closed {Count} stale job(s).",
            message.SeenBefore, closedCount);
    }
}
