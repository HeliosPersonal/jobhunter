using System.Runtime.CompilerServices;
using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of an offline reprocessing run (AC-09, QG-3): the live and closed jobs first seen at or
/// after the cutoff, oldest first, each with the origin raw posting the service re-reads to re-normalise
/// without contacting any provider. Quarantined and superseded jobs are excluded — reprocessing never
/// disturbs a terminal state. Streamed with <c>QueryUnbufferedAsync</c> so a large history recomputes with
/// bounded memory and can meet the ≥ 5 000 postings/min target (QG-3). Dapper, read-only — architecture
/// rule 4 forbids a write in the Queries namespace, which is what keeps this path off the write side.
/// </summary>
public sealed class ReprocessableJobsQuery(INpgsqlConnectionFactory connectionFactory) : IReprocessableJobsQuery
{
    private const string Sql =
        """
        SELECT j.id AS JobId,
               j.company_id AS CompanyId,
               j.origin_raw_posting_id AS OriginRawPostingId,
               j.fingerprint AS Fingerprint
        FROM jobs j
        WHERE j.status IN ('Live', 'Closed')
          AND j.first_seen_at >= @FirstSeenFrom
        ORDER BY j.first_seen_at
        """;

    public async IAsyncEnumerable<ReprocessableJob> StreamAsync(
        DateTimeOffset firstSeenFrom,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        // Unbuffered: rows are read as the caller pulls them, so the whole history is never materialised —
        // this is what keeps a large reprocessing run within bounded memory (QG-3). QueryUnbufferedAsync has
        // only the string overload (no CommandDefinition), which the Dapper.AOT analyser flags with DAP005;
        // we do not opt into AOT interception here, so the warning is suppressed for this one deliberate call.
        // It takes no CancellationToken, so the enumerator's WithCancellation stops the pull between rows.
#pragma warning disable DAP005 // Dapper.AOT interception not enabled — the runtime mapper is intended here.
        var rows = connection.QueryUnbufferedAsync<ReprocessableJob>(Sql, new { FirstSeenFrom = firstSeenFrom });
#pragma warning restore DAP005
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // The fingerprint column is char(64), so it comes back space-padded to a fixed width; trim it so
            // the recomputed 64-char hash compares equal to the stored one (the service compares Ordinal).
            yield return row with { Fingerprint = row.Fingerprint.TrimEnd() };
        }
    }
}
