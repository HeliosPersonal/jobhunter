using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of a single application with its complete history (F6 [[contracts/application-api]]
/// <c>GET /api/applications/{id}</c>, AC-03). Implements <see cref="IApplicationHistoryQuery"/> with Dapper,
/// read-only (architecture rule 4 forbids a write here). One <c>QueryMultiple</c> returns the application
/// header (joined to its job and company for the title and display name), then its transitions ordered oldest
/// first — <c>idx_transitions_application</c> — then its notes newest first — <c>idx_notes_application</c>.
///
/// <para>Retrievable by id whether or not the application is archived, so the full record survives the
/// application leaving the pipeline view (SAD §8 Archival). It selects <strong>nothing about the Owner</strong>
/// — the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class ApplicationHistoryQuery(INpgsqlConnectionFactory connectionFactory) : IApplicationHistoryQuery
{
    private const string Sql =
        """
        SELECT a.id               AS Id,
               a.job_id           AS JobId,
               j.title            AS Title,
               c.display_name     AS Company,
               a.status           AS Status,
               a.posting_closed   AS PostingClosed,
               a.archived         AS Archived,
               a.applied_at       AS AppliedAt,
               a.last_activity_at AS LastActivityAt,
               a.next_action_at   AS NextActionAt
        FROM applications a
        JOIN jobs j ON j.id = a.job_id
        JOIN companies c ON c.id = j.company_id
        WHERE a.id = @Id;

        SELECT t.from_status AS FromStatus,
               t.to_status   AS ToStatus,
               t.source      AS Source,
               t.detail      AS Detail,
               t.occurred_at AS OccurredAt
        FROM application_transitions t
        WHERE t.application_id = @Id
        ORDER BY t.occurred_at, t.id;

        SELECT n.body       AS Body,
               n.created_at AS CreatedAt
        FROM application_notes n
        WHERE n.application_id = @Id
        ORDER BY n.created_at DESC, n.id;
        """;

    public async Task<ApplicationHistory?> HistoryAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Id = applicationId }, cancellationToken: cancellationToken);
        await using var reader = await connection.QueryMultipleAsync(command);

        var header = await reader.ReadSingleOrDefaultAsync<HeaderRow>();
        if (header is null)
        {
            return null;
        }

        var transitions = (await reader.ReadAsync<TransitionRow>())
            .Select(t => new HistoryTransition(
                string.IsNullOrEmpty(t.FromStatus) ? null : Enum.Parse<ApplicationStatus>(t.FromStatus),
                Enum.Parse<ApplicationStatus>(t.ToStatus),
                Enum.Parse<TransitionSource>(t.Source),
                t.Detail,
                t.OccurredAt))
            .ToList();

        var notes = (await reader.ReadAsync<NoteRow>())
            .Select(n => new HistoryNote(n.Body, n.CreatedAt))
            .ToList();

        return new ApplicationHistory(
            header.Id,
            header.JobId,
            header.Title,
            header.Company,
            Enum.Parse<ApplicationStatus>(header.Status),
            header.PostingClosed,
            header.Archived,
            header.AppliedAt,
            header.LastActivityAt,
            header.NextActionAt,
            transitions,
            notes);
    }

    private sealed record HeaderRow
    {
        public Guid Id { get; init; }

        public Guid JobId { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Company { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public bool PostingClosed { get; init; }

        public bool Archived { get; init; }

        public DateTimeOffset? AppliedAt { get; init; }

        public DateTimeOffset LastActivityAt { get; init; }

        public DateTimeOffset? NextActionAt { get; init; }
    }

    private sealed record TransitionRow
    {
        public string? FromStatus { get; init; }

        public string ToStatus { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string? Detail { get; init; }

        public DateTimeOffset OccurredAt { get; init; }
    }

    private sealed record NoteRow
    {
        public string Body { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }
    }
}
