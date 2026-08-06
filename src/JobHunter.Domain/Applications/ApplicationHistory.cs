namespace JobHunter.Domain.Applications;

/// <summary>
/// The single-application read model (F6 [[contracts/application-api]] <c>GET /api/applications/{id}</c>,
/// AC-03): one application with its complete, ordered transition history and its notes. Assembled by
/// <c>IApplicationHistoryQuery</c> and retrievable by id even when archived, so the full record survives the
/// application leaving the pipeline view (SAD §8 Archival).
///
/// <para>It carries <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is
/// not this one (F4 invariant).</para>
/// </summary>
/// <param name="Id">The application id.</param>
/// <param name="JobId">The job the application tracks.</param>
/// <param name="Title">The job title (raw board text; escaped and truncated by the renderer).</param>
/// <param name="Company">The company display name.</param>
/// <param name="Status">The current status.</param>
/// <param name="PostingClosed">Whether the underlying posting has closed (AC-07).</param>
/// <param name="Archived">Whether the application has archived out of the pipeline view.</param>
/// <param name="AppliedAt">When the Owner first applied, or null.</param>
/// <param name="LastActivityAt">The most recent activity.</param>
/// <param name="NextActionAt">When a reminder is next due, or null.</param>
/// <param name="Transitions">Every status change, oldest first, including the creating <c>New</c> row (QG-1).</param>
/// <param name="Notes">The Owner's free-text notes, newest first.</param>
public sealed record ApplicationHistory(
    Guid Id,
    Guid JobId,
    string Title,
    string Company,
    ApplicationStatus Status,
    bool PostingClosed,
    bool Archived,
    DateTimeOffset? AppliedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? NextActionAt,
    IReadOnlyList<HistoryTransition> Transitions,
    IReadOnlyList<HistoryNote> Notes);

/// <summary>One recorded status change: the move, its source and when it happened (AC-03).</summary>
/// <param name="From">The status moved from, or null for the creating transition.</param>
/// <param name="To">The status moved to.</param>
/// <param name="Source">Where the change came from — <c>Telegram</c>, <c>Api</c> or <c>System</c>.</param>
/// <param name="Detail">Optional free text recorded with the change, or null.</param>
/// <param name="OccurredAt">When the change happened — the history's ordering key.</param>
public sealed record HistoryTransition(
    ApplicationStatus? From,
    ApplicationStatus To,
    TransitionSource Source,
    string? Detail,
    DateTimeOffset OccurredAt);

/// <summary>One free-text note the Owner attached, and when.</summary>
/// <param name="Body">The note text (never logged — only its length is, invariant 12).</param>
/// <param name="CreatedAt">When the note was added.</param>
public sealed record HistoryNote(string Body, DateTimeOffset CreatedAt);
