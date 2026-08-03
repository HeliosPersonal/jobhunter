using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Postings;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of one stored raw posting's normalisable content (data-model §raw_postings). Serves F2
/// normalisation and reprocessing: it selects the verbatim <c>payload</c> and the timestamps that become a
/// job's first/last seen, keyed by id (the primary key, so the read is a single index probe). Dapper, flat
/// DTO, read-only — architecture rule 4 forbids a write method in the Queries namespace, which is exactly
/// what keeps the F1-owned, immutable <c>raw_postings</c> table safe from this path (SAD §2, invariant 1).
/// </summary>
public sealed class RawPostingReaderQuery(INpgsqlConnectionFactory connectionFactory) : IRawPostingReader
{
    private const string Sql =
        """
        SELECT id AS Id,
               source_id AS SourceId,
               external_id AS ExternalId,
               payload AS Payload,
               fetched_at AS FetchedAt,
               last_seen_at AS LastSeenAt
        FROM raw_postings
        WHERE id = @Id
        """;

    public async Task<RawPostingContent?> FindAsync(
        Guid rawPostingId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql, new { Id = rawPostingId }, cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<RawPostingContent>(command);
    }
}
