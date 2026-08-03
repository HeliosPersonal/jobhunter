using JobHunter.Domain.Intelligence;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write repository for the <see cref="Enrichment"/> aggregate (data-model §enrichments). Its only
/// write path is an idempotent upsert keyed on <c>(job_id, run_id)</c>: an <see cref="Enrichment"/> is
/// immutable, and a correction is a new row for a new Run, so replaying a half-processed result set is
/// safe by construction rather than by luck (AC-06, invariant 3). There is deliberately no update path —
/// the second write of the same key is a no-op, not an overwrite.
/// </summary>
public interface IEnrichmentRepository
{
    /// <summary>
    /// Persists <paramref name="enrichment"/> if no row exists for its <c>(job_id, run_id)</c>, and does
    /// nothing if one already does. Returns <see langword="true"/> when a row was written,
    /// <see langword="false"/> on the idempotent no-op — so a resumed result pass can tell new work from
    /// replayed work in one round trip.
    /// </summary>
    Task<bool> UpsertAsync(Enrichment enrichment, CancellationToken cancellationToken = default);

    /// <summary>Finds the enrichment for a job in a given Run, or null.</summary>
    Task<Enrichment?> FindAsync(Guid jobId, Guid runId, CancellationToken cancellationToken = default);
}
