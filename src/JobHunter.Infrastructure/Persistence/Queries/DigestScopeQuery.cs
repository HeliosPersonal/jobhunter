using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Infrastructure.Persistence.Pipeline;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "the scores a Run's digest draws on" (F5 SAD §6.1, data-model §scores/§matches).
/// Implements <see cref="IDigestScopeQuery"/> with Dapper, read-only (architecture rule 4 forbids a write
/// here): one row per <c>scores</c> row in the Run — shown or suppressed — joined with a
/// <c>LEFT JOIN LATERAL</c> to the job's current match for the reasons the card explains itself with and the
/// salary expectation the header averages, and joined to <c>jobs</c> for the apply URL the card links to and
/// verifies before it is shown (AC-11).
///
/// <para>It returns <em>every</em> score, because the suppression breakdown is built from the suppressed ones
/// (invariant 11, AC-07): dropping them here would make the footer a silent lie. Ordered by
/// <c>final_score DESC, job_id</c> — served by <c>idx_scores_run_final</c> for the shown set — so card
/// selection and the top-ten cut are deterministic (QG-3). Only a USD-denominated salary expectation is
/// surfaced as <see cref="DigestCandidate.SalaryUsd"/>; a non-USD figure is left null rather than converted,
/// because a fabricated FX rate is worse than an absent average. It selects <strong>nothing about the
/// Owner</strong> — the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class DigestScopeQuery(INpgsqlConnectionFactory connectionFactory) : IDigestScopeQuery
{
    private const string Sql =
        """
        SELECT s.job_id           AS JobId,
               s.final_score      AS FinalScore,
               s.suppressed       AS Suppressed,
               s.suppression_reason AS SuppressionReason,
               m.reasons          AS ReasonsJson,
               CASE
                   WHEN m.salary_expectation_currency = 'USD'
                        AND m.salary_expectation_min IS NOT NULL
                        AND m.salary_expectation_max IS NOT NULL
                   THEN (m.salary_expectation_min + m.salary_expectation_max) / 2
                   ELSE NULL
               END                AS SalaryUsd,
               j.apply_url        AS ApplyUrl
        FROM scores s
        JOIN jobs j ON j.id = s.job_id
        LEFT JOIN LATERAL (
            SELECT mm.reasons,
                   mm.salary_expectation_currency,
                   mm.salary_expectation_min,
                   mm.salary_expectation_max
            FROM matches mm
            WHERE mm.job_id = s.job_id
              AND mm.is_current
            ORDER BY mm.created_at DESC
            LIMIT 1
        ) m ON TRUE
        WHERE s.run_id = @RunId
        ORDER BY s.final_score DESC, s.job_id
        """;

    public async Task<IReadOnlyList<DigestCandidate>> CandidatesAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { RunId = runId }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<CandidateRow>(command);

        return rows
            .Select(r => new DigestCandidate(
                r.JobId,
                r.FinalScore,
                r.Suppressed,
                r.SuppressionReason,
                StringListJson.Deserialize(r.ReasonsJson),
                r.SalaryUsd,
                r.ApplyUrl))
            .ToList();
    }

    private sealed record CandidateRow(
        Guid JobId,
        decimal FinalScore,
        bool Suppressed,
        string? SuppressionReason,
        string? ReasonsJson,
        decimal? SalaryUsd,
        string ApplyUrl);
}
