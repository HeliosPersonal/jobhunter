using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the pipeline view (F6 [[contracts/application-api]] §Pipeline response, AC-01). Implements
/// <see cref="IApplicationPipelineQuery"/> with Dapper, read-only (architecture rule 4 forbids a write here):
/// one row per non-archived application, joined to its job for the title, to <c>companies</c> for the display
/// name, and — with a <c>LEFT JOIN LATERAL</c> — to the job's latest score for the number the card shows and
/// to its transition history for when the current stage was entered.
///
/// <para>The rows come back ordered by <c>status</c> then <c>last_activity_at DESC</c> — the exact shape of
/// the partial <c>idx_applications_pipeline</c> — so grouping in memory preserves the most-recently-active
/// order and the whole read is index-covered. Archived applications are excluded (SAD §8 Archival);
/// <see cref="PipelineEntry.DaysInStage"/> is computed here from the caller's <c>now</c>, never stored. It
/// selects <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is not this
/// one (F4 invariant).</para>
/// </summary>
public sealed class ApplicationPipelineQuery(INpgsqlConnectionFactory connectionFactory) : IApplicationPipelineQuery
{
    internal const string Sql =
        """
        SELECT a.id                        AS Id,
               a.job_id                    AS JobId,
               j.title                     AS Title,
               c.display_name              AS Company,
               a.status                    AS Status,
               a.posting_closed            AS PostingClosed,
               a.applied_at                AS AppliedAt,
               a.last_activity_at          AS LastActivityAt,
               a.next_action_at            AS NextActionAt,
               COALESCE(sc.final_score, 0) AS Score,
               st.stage_entered_at         AS StageEnteredAt
        FROM applications a
        JOIN jobs j ON j.id = a.job_id
        JOIN companies c ON c.id = j.company_id
        LEFT JOIN LATERAL (
            SELECT s.final_score
            FROM scores s
            WHERE s.job_id = a.job_id
            ORDER BY s.computed_at DESC
            LIMIT 1
        ) sc ON TRUE
        LEFT JOIN LATERAL (
            SELECT max(t.occurred_at) AS stage_entered_at
            FROM application_transitions t
            WHERE t.application_id = a.id
        ) st ON TRUE
        WHERE NOT a.archived
        ORDER BY a.status, a.last_activity_at DESC
        """;

    public async Task<ApplicationPipeline> PipelineAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<PipelineRow>(command);

        // Rows arrive ordered by status then last_activity_at DESC (index order), so a preserving group-by
        // yields each status column already most-recently-active first.
        var groups = rows
            .GroupBy(r => Enum.Parse<ApplicationStatus>(r.Status))
            .Select(g => new PipelineGroup(
                g.Key,
                g.Select(r => new PipelineEntry(
                    r.Id,
                    r.JobId,
                    r.Title,
                    r.Company,
                    r.Score,
                    r.PostingClosed,
                    r.AppliedAt,
                    r.LastActivityAt,
                    r.NextActionAt,
                    DaysInStage(r.StageEnteredAt, now))).ToList()))
            .ToList();

        return new ApplicationPipeline(groups);
    }

    // Whole days since the current stage was entered — the most recent transition's instant. A negative
    // span (a clock skew) floors at zero rather than showing a nonsensical count.
    private static int DaysInStage(DateTimeOffset stageEnteredAt, DateTimeOffset now)
    {
        var days = (now - stageEnteredAt).Days;
        return days < 0 ? 0 : days;
    }

    private sealed record PipelineRow
    {
        public Guid Id { get; init; }

        public Guid JobId { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Company { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public bool PostingClosed { get; init; }

        public DateTimeOffset? AppliedAt { get; init; }

        public DateTimeOffset LastActivityAt { get; init; }

        public DateTimeOffset? NextActionAt { get; init; }

        public decimal Score { get; init; }

        public DateTimeOffset StageEnteredAt { get; init; }
    }
}
