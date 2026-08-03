using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Postings;
using Npgsql;
using NpgsqlTypes;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// Ingests raw postings with the single-statement dedup-and-refresh upsert (data-model §raw_postings,
/// AC-02). This is a write, so it uses Npgsql directly rather than Dapper — "Dapper never writes"
/// (ADR-0003) is about the read-side Queries namespace, not the write repositories. Implements the
/// <see cref="IRawPostingRepository"/> port defined in Domain.
///
/// The statement is <c>INSERT … ON CONFLICT (source_id, external_id, content_hash) DO UPDATE SET
/// last_seen_at = EXCLUDED.last_seen_at</c>, and it returns <c>xmax = 0</c> to tell a genuine insert
/// (a fresh row, <c>xmax</c> zero) from a conflict update (an existing row, <c>xmax</c> non-zero). That
/// is what makes <c>RawPostingIngested</c> fire exactly once per distinct content, with no read-then-write
/// race (invariant 8, AC-02).
/// </summary>
public sealed class RawPostingRepository(INpgsqlConnectionFactory connectionFactory) : IRawPostingRepository
{
    private const string UpsertSql =
        """
        INSERT INTO raw_postings
            (id, source_id, external_id, content_hash, payload, http_status, fetched_at, last_seen_at)
        VALUES
            (@id, @source_id, @external_id, @content_hash, @payload, @http_status, @fetched_at, @last_seen_at)
        ON CONFLICT (source_id, external_id, content_hash)
        DO UPDATE SET last_seen_at = EXCLUDED.last_seen_at
        RETURNING (xmax = 0) AS inserted;
        """;

    // The 90-day retention prune (T09, O3): delete only postings gone cold — last seen before the cutoff —
    // and, defensively, only those no live/closed job still references through job_aliases. The NOT EXISTS
    // makes the intent explicit in SQL; the restrict FK on job_aliases → raw_postings is the backstop that
    // would refuse a delete even if this clause were ever wrong, so a referenced posting is never removed.
    private const string PruneSql =
        """
        DELETE FROM raw_postings r
        WHERE r.last_seen_at < @older_than
          AND NOT EXISTS (
              SELECT 1 FROM job_aliases a WHERE a.raw_posting_id = r.id
          );
        """;

    public async Task<IngestOutcome> IngestAsync(RawPosting posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(posting);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(UpsertSql, connection);

        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = posting.Id });
        command.Parameters.Add(new NpgsqlParameter("source_id", NpgsqlDbType.Uuid) { Value = posting.SourceId });
        command.Parameters.Add(new NpgsqlParameter("external_id", NpgsqlDbType.Text) { Value = posting.ExternalId });
        command.Parameters.Add(new NpgsqlParameter("content_hash", NpgsqlDbType.Char) { Value = posting.ContentHash.Value });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = posting.Payload });
        command.Parameters.Add(new NpgsqlParameter("http_status", NpgsqlDbType.Smallint) { Value = posting.HttpStatus });
        command.Parameters.Add(new NpgsqlParameter("fetched_at", NpgsqlDbType.TimestampTz) { Value = posting.FetchedAt });
        command.Parameters.Add(new NpgsqlParameter("last_seen_at", NpgsqlDbType.TimestampTz) { Value = posting.LastSeenAt });

        var inserted = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        return inserted ? IngestOutcome.Inserted : IngestOutcome.Unchanged;
    }

    public async Task<int> PruneOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(PruneSql, connection);
        command.Parameters.Add(new NpgsqlParameter("older_than", NpgsqlDbType.TimestampTz) { Value = olderThan });

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
