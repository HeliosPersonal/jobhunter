using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the reminder sweep (F6 SAD §6.2, T06). Implements <see cref="IDueReminderQuery"/> with
/// Dapper, read-only (architecture rule 4 forbids a write here): the non-archived applications whose
/// <c>next_action_at</c> has passed, joined to their job for the title and apply-url and to <c>companies</c>
/// for the display name. The <c>WHERE next_action_at IS NOT NULL AND NOT archived AND next_action_at &lt;=
/// @now</c> predicate is the exact shape of the partial <c>idx_applications_due</c>, so the sweep is one
/// indexed range read, never a scan (done-when 5, SAD §4 S6).
///
/// <para>It returns every due row and carries <c>last_reminder_condition</c> so the handler can decide
/// suppression (one per condition, QG-3) without a second query. It selects <strong>nothing about the
/// Owner</strong> — the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class DueReminderQuery(INpgsqlConnectionFactory connectionFactory) : IDueReminderQuery
{
    internal const string Sql =
        """
        SELECT a.id                     AS ApplicationId,
               a.job_id                 AS JobId,
               j.title                  AS Title,
               c.display_name           AS Company,
               j.apply_url              AS ApplyUrl,
               a.status                 AS Status,
               a.posting_closed         AS PostingClosed,
               a.last_reminder_condition AS LastReminderCondition
        FROM applications a
        JOIN jobs j ON j.id = a.job_id
        JOIN companies c ON c.id = j.company_id
        WHERE a.next_action_at IS NOT NULL
          AND NOT a.archived
          AND a.next_action_at <= @now
        ORDER BY a.next_action_at
        """;

    public async Task<IReadOnlyList<DueReminder>> DueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { now }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<DueRow>(command);

        return rows
            .Select(r => new DueReminder(
                r.ApplicationId,
                r.JobId,
                r.Title,
                r.Company,
                r.ApplyUrl,
                Enum.Parse<ApplicationStatus>(r.Status),
                r.PostingClosed,
                r.LastReminderCondition))
            .ToList();
    }

    private sealed record DueRow
    {
        public Guid ApplicationId { get; init; }

        public Guid JobId { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Company { get; init; } = string.Empty;

        public string ApplyUrl { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public bool PostingClosed { get; init; }

        public string? LastReminderCondition { get; init; }
    }
}
