using JobHunter.Domain.Intelligence;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write repository for the <see cref="Match"/> aggregate (data-model §matches). Its only insert
/// path is an idempotent upsert keyed on <c>(job_id, run_id, profile_id)</c>: replaying a half-processed
/// result set writes each match exactly once (invariant 3), so a resumed pass is safe by construction
/// rather than by luck. A match is immutable except for its <c>is_current</c> flag, which the re-staling
/// sweep clears when its CV version is superseded (AC-08) — it is never deleted.
/// </summary>
public interface IMatchRepository
{
    /// <summary>
    /// Persists <paramref name="match"/> if no row exists for its <c>(job_id, run_id, profile_id)</c>, and
    /// does nothing if one already does. Returns <see langword="true"/> when a row was written,
    /// <see langword="false"/> on the idempotent no-op.
    /// </summary>
    Task<bool> UpsertAsync(Match match, CancellationToken cancellationToken = default);

    /// <summary>Finds the match for a job in a given Run and profile, or null.</summary>
    Task<Match?> FindAsync(Guid jobId, Guid runId, Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks every current match made against <paramref name="cvVersionId"/> as no longer current (AC-08).
    /// Returns the number of matches marked stale.
    /// </summary>
    Task<int> MarkNotCurrentForCvVersionAsync(Guid cvVersionId, CancellationToken cancellationToken = default);
}
