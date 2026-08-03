using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Postings;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the closure sweep (SAD §6.1, T13): the raw postings whose <c>last_seen_at</c> is
/// strictly before the cutoff — not re-seen this cycle, so gone from their board. Only the most recent row
/// per <c>(source_id, external_id)</c> is considered live: an earlier content revision that was superseded
/// is not itself a closure, so <c>DISTINCT ON</c> keeps the latest and the sweep judges liveness on it.
/// Dapper, flat DTO, read-only (architecture rule 4 forbids a write method here); implements the port.
/// </summary>
public sealed class ClosureSweepQuery(INpgsqlConnectionFactory connectionFactory) : IClosureSweepQuery
{
    private const string Sql =
        """
        SELECT latest.id AS RawPostingId, latest.last_seen_at AS LastSeenAt
        FROM (
            SELECT DISTINCT ON (source_id, external_id) id, last_seen_at
            FROM raw_postings
            ORDER BY source_id, external_id, last_seen_at DESC
        ) AS latest
        WHERE latest.last_seen_at < @SeenBefore
        ORDER BY latest.id
        """;

    public async Task<IReadOnlyList<ClosedPosting>> ClosedSinceAsync(
        DateTimeOffset seenBefore,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { SeenBefore = seenBefore }, cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<ClosedPosting>(command);
        return rows.AsList();
    }
}
