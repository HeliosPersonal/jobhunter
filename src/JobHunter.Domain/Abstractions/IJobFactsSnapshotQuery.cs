using JobHunter.Domain.Preferences;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// Reads a job's characteristics <em>as they are right now</em> into the <see cref="JobFacts"/> vocabulary
/// the preference learner fits on (F5 T10 AC-08, F7 data-model §signals). It exists so a card action can
/// snapshot the job at the moment of the tap rather than joining to <c>jobs</c> at fitting time — a later
/// edit to the job must not rewrite what the Owner is recorded as having reacted to.
///
/// <para>Returns <c>null</c> when the job no longer exists or has closed, which the caller turns into the
/// plain "this role has closed" acknowledgement and records nothing invalid (AC-09).</para>
/// </summary>
public interface IJobFactsSnapshotQuery
{
    /// <summary>
    /// The job's current facts as a snapshot, or <c>null</c> when the job is gone or closed. Never throws for
    /// a missing job — absence is a value the caller reacts to, not an exception.
    /// </summary>
    Task<JobFacts?> SnapshotAsync(Guid jobId, CancellationToken cancellationToken = default);
}
