using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "the current matches a Run's ranking pass scores" (F4 SAD §6.2, data-model §matches/§scores).
/// Implements <see cref="IRankingScopeQuery"/> with Dapper, read-only (architecture rule 4 forbids a write here):
/// it selects one row per <em>current</em> match in the Run — the model's fit judgement — joined to the job's
/// <c>first_seen_at</c> (for freshness) and, with a <c>LEFT JOIN LATERAL</c>, the job's latest enrichment across
/// any Run (for the confidence multiplier and the opt-in salary-floor down-weight).
///
/// <para>Scoped to <c>run_id = @RunId AND m.is_current</c>: a match superseded by a CV re-upload mid-Run is not
/// ranked (AC-08). A matched-but-unenriched job comes back with a null enrichment rather than being dropped, so
/// it is still ranked at a discounted confidence (AC-09). Ordered by job id so a re-run sees the same items in the
/// same order (QG-3). It selects <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary,
/// and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class RankingScopeQuery(INpgsqlConnectionFactory connectionFactory) : IRankingScopeQuery
{
    private const string Sql =
        """
        SELECT m.job_id            AS JobId,
               m.match_score::int  AS MatchScore,
               j.first_seen_at     AS FirstSeenAt,
               e.id                AS EnrichmentId,
               e.salary_min        AS EstSalaryMin,
               e.salary_max        AS EstSalaryMax,
               e.salary_currency   AS EstSalaryCurrency,
               e.salary_period     AS EstSalaryPeriod,
               e.salary_confidence AS EstSalaryConfidence
        FROM matches m
        JOIN jobs j ON j.id = m.job_id
        LEFT JOIN LATERAL (
            SELECT en.*
            FROM enrichments en
            WHERE en.job_id = m.job_id
            ORDER BY en.created_at DESC
            LIMIT 1
        ) e ON TRUE
        WHERE m.run_id = @RunId
          AND m.is_current
        ORDER BY m.job_id
        """;

    public async Task<IReadOnlyList<RankingJob>> InScopeAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { RunId = runId }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<ScopeRow>(command);

        return rows
            .Select(r => new RankingJob(
                r.JobId,
                r.MatchScore,
                r.FirstSeenAt,
                r.EnrichmentId is not null,
                ToEstimate(r)))
            .ToList();
    }

    private static SalaryEstimate? ToEstimate(ScopeRow r)
    {
        if (r.EnrichmentId is null || r.EstSalaryMin is not { } min || r.EstSalaryMax is not { } max)
        {
            return null;
        }

        var period = Enum.TryParse<SalaryPeriod>(r.EstSalaryPeriod, ignoreCase: false, out var parsed)
            ? parsed
            : SalaryPeriod.Year;
        var confidence = r.EstSalaryConfidence ?? 0m;
        var result = SalaryEstimate.TryCreate(min, max, r.EstSalaryCurrency, period, confidence);

        // The value was valid when it was persisted; if a stored row is somehow unrepresentable the salary is
        // simply dropped and the job still ranked, never thrown.
        return result.IsSuccess ? result.Value : null;
    }

    private sealed record ScopeRow(
        Guid JobId,
        int MatchScore,
        DateTimeOffset FirstSeenAt,
        Guid? EnrichmentId,
        decimal? EstSalaryMin,
        decimal? EstSalaryMax,
        string? EstSalaryCurrency,
        string? EstSalaryPeriod,
        decimal? EstSalaryConfidence);
}
