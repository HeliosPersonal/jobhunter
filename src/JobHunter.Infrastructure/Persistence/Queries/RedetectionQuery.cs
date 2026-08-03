using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of weekly binding re-detection (SAD §6.2, AC-05): the companies to re-probe this run. A
/// company qualifies when its live binding is stale (detected before the cutoff) or when its board's last
/// <c>emptyCycles</c> successful fetches all returned zero postings — the "two consecutive empty cycles"
/// signal that a board legitimately holding openings does not trip. Both are scoped to the day's bucket so
/// the weekly re-probe is spread across the week: a company falls into a fixed bucket by a hash of its id
/// (<c>abs(hashtextextended(id::text, 0)) % bucketCount</c>), and a run reads only <c>@DayBucket</c>.
/// Dapper, flat DTO, read-only (architecture rule 4 forbids a write method here); implements the port.
/// </summary>
public sealed class RedetectionQuery(INpgsqlConnectionFactory connectionFactory) : IRedetectionQuery
{
    // A company is a candidate if it has any live binding that is stale, OR its source's most recent
    // @EmptyCycles successful fetches all returned zero postings. The empty-cycle check uses source_fetch_log
    // rows with outcome 'Success', newest first: a company with fewer than @EmptyCycles successes does not
    // qualify on that basis (it has not had two full cycles to be empty). The day-bucket filter spreads the
    // week — the same stable hash the handler uses to pick @DayBucket.
    private const string Sql =
        """
        WITH live AS (
            SELECT b.company_id, b.detected_at
            FROM ats_bindings b
            WHERE b.retired_at IS NULL
        ),
        empty_boards AS (
            SELECT s.company_id
            FROM job_sources s
            WHERE (
                SELECT COALESCE(bool_and(recent.postings_returned = 0), false)
                FROM (
                    SELECT l.postings_returned
                    FROM source_fetch_log l
                    WHERE l.source_id = s.id AND l.outcome = 'Success'
                    ORDER BY l.started_at DESC
                    LIMIT @EmptyCycles
                ) AS recent
                HAVING count(*) = @EmptyCycles
            )
        ),
        candidates AS (
            SELECT company_id FROM live WHERE detected_at < @StaleBefore
            UNION
            SELECT company_id FROM empty_boards
        )
        SELECT c.company_id AS CompanyId
        FROM candidates c
        WHERE ((hashtextextended(c.company_id::text, 0) % @BucketCount) + @BucketCount) % @BucketCount = @DayBucket
        ORDER BY c.company_id
        """;

    public async Task<IReadOnlyList<RedetectionCandidate>> DueCandidatesAsync(
        DateTimeOffset staleBefore,
        int emptyCycles,
        int dayBucket,
        int bucketCount,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql,
            new
            {
                StaleBefore = staleBefore,
                EmptyCycles = emptyCycles,
                DayBucket = dayBucket,
                BucketCount = bucketCount,
            },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<RedetectionCandidate>(command);
        return rows.AsList();
    }
}
