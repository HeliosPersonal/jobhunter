using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Abstractions;

/// <summary>The outcome of a conflict-tolerant job insert: whether a genuinely new job row was created.</summary>
public enum JobInsertOutcome
{
    /// <summary>A new job was created; the deduplication handler publishes <c>JobDiscovered</c>.</summary>
    Inserted,

    /// <summary>
    /// The fingerprint was already present — a concurrent consumer won the race, or the same opening was
    /// seen on a second board. The caller loads the existing job and registers an alias instead (AC-08).
    /// </summary>
    FingerprintConflict,
}

/// <summary>
/// The write port for the canonical job aggregate (data-model §jobs, §job_aliases, §job_technologies).
/// The <see cref="InsertAsync"/> path is the concurrency arbiter: it inserts the job with
/// <c>ON CONFLICT (fingerprint) DO NOTHING</c> and reports in a single round trip whether it inserted or
/// conflicted — no read-then-write, so two consumers racing on one opening produce exactly one job with
/// no lock (SAD §6.1, invariant 2). Defined in Domain so the deduplication handler depends on the port;
/// Infrastructure supplies the Npgsql/EF implementation.
/// </summary>
public interface IJobRepository
{
    /// <summary>
    /// Inserts <paramref name="job"/> and its aliases and technologies atomically. Returns
    /// <see cref="JobInsertOutcome.Inserted"/> on a genuine insert, or
    /// <see cref="JobInsertOutcome.FingerprintConflict"/> when the fingerprint already exists — in which
    /// case nothing is written.
    /// </summary>
    Task<JobInsertOutcome> InsertAsync(Job job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the tracked job carrying <paramref name="fingerprint"/>, with its aliases and technologies,
    /// or null when none exists. Used by the conflict path to register a new alias on the winning job.
    /// </summary>
    Task<Job?> FindByFingerprintAsync(Fingerprint fingerprint, CancellationToken cancellationToken = default);

    /// <summary>Loads the tracked job by id, with its aliases and technologies, or null.</summary>
    Task<Job?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Persists changes made to tracked jobs (alias registration, lifecycle transitions).</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
