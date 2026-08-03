using JobHunter.Domain.Postings;

namespace JobHunter.Domain.Abstractions;

/// <summary>The outcome of an idempotent ingest: whether a genuinely new row was inserted.</summary>
public enum IngestOutcome
{
    /// <summary>A new posting row was created; the caller publishes <c>RawPostingIngested</c>.</summary>
    Inserted,

    /// <summary>The exact content was already present; only <c>last_seen_at</c> moved (AC-02) — no event.</summary>
    Unchanged,
}

/// <summary>
/// The write port that ingests raw postings with the single-statement dedup-and-refresh upsert
/// (data-model §raw_postings, AC-02). The statement is <c>INSERT … ON CONFLICT (source_id, external_id,
/// content_hash) DO UPDATE SET last_seen_at = EXCLUDED.last_seen_at</c>, returning <c>xmax = 0</c> to tell
/// a genuine insert from a conflict update — which is what makes <c>RawPostingIngested</c> fire exactly
/// once per distinct content, with no read-then-write race (invariant 8, AC-02). Defined in Domain so the
/// ingestion handler depends on the port; Infrastructure supplies the Npgsql implementation.
/// </summary>
public interface IRawPostingRepository
{
    Task<IngestOutcome> IngestAsync(RawPosting posting, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes raw postings last seen strictly before <paramref name="olderThan"/> — the 90-day retention job
    /// (T09, O3). A posting still referenced by a <c>job_aliases</c> row is never removed: it is the
    /// provenance a live or closed job depends on, and the <c>job_aliases → raw_postings</c> restrict FK
    /// enforces that at the database besides. Returns the number of rows deleted, so the operator command
    /// reports its work. Idempotent — a second run over the same cutoff prunes nothing more.
    /// </summary>
    Task<int> PruneOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
