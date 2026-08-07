using Dapper;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of <c>/floor</c>'s preview (F10 T08, catalogue §Profile): counts how many of the latest Run's
/// shown jobs the proposed floor <em>would have</em> affected. Implements <see cref="ISalaryFloorPreviewQuery"/>
/// with Dapper, read-only (architecture rule 4 forbids a write here). The definition of "affected" mirrors the
/// ranking's <c>SuppressionEvaluator.BelowSalaryFloor</c> rule exactly, so the preview cannot promise a different
/// outcome than the Run would deliver: the latest Run's non-suppressed <c>scores</c>, joined by
/// <c>LEFT JOIN LATERAL</c> to each job's most recent <c>enrichments</c> row, where that estimate is
/// <strong>high-confidence</strong> (<c>salary_confidence &gt;= 0.7</c>, the <c>HighConfidence</c> const),
/// <strong>same-currency</strong> (never a cross-currency lie) and sits <strong>wholly below</strong> the floor
/// (<c>salary_max &lt; @Floor</c> — even the top of the band misses it).
///
/// <para>Scoped to the latest Run — the one with the greatest <c>started_at</c> — so yesterday's below-floor set
/// does not linger; a suppressed row never counts, because it is already withheld and the floor does not change
/// what the digest shows. The currency is compared on the trimmed <c>char(3)</c> column against the caller's
/// already-upper-cased code. It selects <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one
/// boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class SalaryFloorPreviewQuery(INpgsqlConnectionFactory connectionFactory) : ISalaryFloorPreviewQuery
{
    private const decimal HighConfidence = 0.7m;

    private const string Sql =
        """
        SELECT count(*)
        FROM scores s
        JOIN LATERAL (
            SELECT ee.salary_max, ee.salary_currency, ee.salary_confidence
            FROM enrichments ee
            WHERE ee.job_id = s.job_id
            ORDER BY ee.created_at DESC
            LIMIT 1
        ) e ON TRUE
        WHERE NOT s.suppressed
          AND s.run_id = (
              SELECT r.id FROM runs r
              ORDER BY r.started_at DESC NULLS LAST
              LIMIT 1
          )
          AND e.salary_confidence >= @HighConfidence
          AND e.salary_max IS NOT NULL
          AND rtrim(e.salary_currency) = @Currency
          AND e.salary_max < @Floor
        """;

    public async Task<int> CountAffectedAsync(
        decimal floor, string currency, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql,
            new { Floor = floor, Currency = currency.Trim(), HighConfidence },
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<int>(command);
    }
}
