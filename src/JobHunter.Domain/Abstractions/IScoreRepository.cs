using JobHunter.Domain.Intelligence;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write repository for the <see cref="Score"/> aggregate (data-model §scores). Its only insert path
/// is an idempotent upsert keyed on the composite <c>(job_id, run_id)</c>: ranking a Run twice writes
/// each score exactly once, so a resumed ranking pass is safe by construction. A score is the output of
/// arithmetic — every component is stored so the total can be reconstructed (QG-1) — and a row may exist
/// with no matching <c>matches</c> row when a job was excluded before matching.
/// </summary>
public interface IScoreRepository
{
    /// <summary>
    /// Persists <paramref name="score"/> if no row exists for its <c>(job_id, run_id)</c>, and does
    /// nothing if one already does. Returns <see langword="true"/> when a row was written,
    /// <see langword="false"/> on the idempotent no-op.
    /// </summary>
    Task<bool> UpsertAsync(Score score, CancellationToken cancellationToken = default);

    /// <summary>Finds the score for a job in a given Run, or null.</summary>
    Task<Score?> FindAsync(Guid jobId, Guid runId, CancellationToken cancellationToken = default);
}
