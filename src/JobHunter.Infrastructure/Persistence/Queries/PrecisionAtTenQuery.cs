using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the <c>precision@10</c> comparison (F7 T09 done-when 4, AC-08). Implements
/// <see cref="IPrecisionAtTenQuery"/> with Dapper, read-only (architecture rule 4 forbids a write here). For
/// each Run it ranks the <em>shown</em> scores (<c>NOT suppressed</c>) by <c>final_score</c>, keeps the top
/// ten, and asks how many of those jobs drew a positive reaction from the Owner — an <c>Opened</c>,
/// <c>Saved</c>, <c>Applied</c>, <c>Interview</c> or <c>Offer</c> signal on the job. Precision is the hit count
/// over the considered count; the Run is bucketed <c>after_activation</c> when its shown scores carried a
/// <c>preference_model_id</c>, so the series splits cleanly into before-and-after halves.
///
/// <para>Only shown rows count — a suppressed job the Owner later opened is regret, measured elsewhere (T09
/// done-when 5), not precision. A Run that showed nothing yields no row. The series is oldest Run first, so a
/// plot reads left to right. It selects <strong>nothing about the Owner's CV</strong> — the CV crosses exactly
/// one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class PrecisionAtTenQuery(INpgsqlConnectionFactory connectionFactory) : IPrecisionAtTenQuery
{
    /// <summary>The reactions that count as engagement — a glance turned into an act, or an application outcome.</summary>
    private static readonly string[] PositiveKinds =
    [
        SignalKind.Opened.ToString(),
        SignalKind.Saved.ToString(),
        SignalKind.Applied.ToString(),
        SignalKind.Interview.ToString(),
        SignalKind.Offer.ToString(),
    ];

    private const string Sql =
        """
        WITH ranked AS (
            SELECT s.job_id,
                   s.run_id,
                   s.preference_model_id,
                   ROW_NUMBER() OVER (PARTITION BY s.run_id ORDER BY s.final_score DESC, s.job_id) AS rn
            FROM scores s
            WHERE NOT s.suppressed
        ),
        top_ten AS (
            SELECT r.run_id,
                   r.preference_model_id,
                   EXISTS (
                       SELECT 1 FROM signals sig
                       WHERE sig.job_id = r.job_id
                         AND sig.kind = ANY(@Positive)
                   ) AS is_hit
            FROM ranked r
            WHERE r.rn <= 10
        )
        SELECT t.run_id                                          AS RunId,
               run.started_at                                   AS RunStartedAt,
               bool_or(t.preference_model_id IS NOT NULL)       AS AfterActivation,
               COUNT(*)::int                                    AS Considered,
               COUNT(*) FILTER (WHERE t.is_hit)::int            AS Hits,
               ROUND(COUNT(*) FILTER (WHERE t.is_hit)::numeric / COUNT(*), 4) AS Precision
        FROM top_ten t
        JOIN runs run ON run.id = t.run_id
        GROUP BY t.run_id, run.started_at
        ORDER BY run.started_at, t.run_id
        """;

    public async Task<IReadOnlyList<PrecisionAtTenPoint>> SeriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Positive = PositiveKinds }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<PrecisionAtTenPoint>(command);

        return rows.ToList();
    }
}
