namespace JobHunter.Domain.Applications;

/// <summary>
/// The pipeline read model (F6 [[contracts/application-api]] §Pipeline response, AC-01): the non-archived
/// applications grouped by their current status, each group ordered most-recently-active first. It is a
/// projection assembled by <c>IApplicationPipelineQuery</c>, not the aggregate — the Telegram and API layers
/// render it, and derive the per-status counts from the group sizes.
///
/// <para>It carries <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is
/// not this one (F4 invariant).</para>
/// </summary>
/// <param name="Groups">One group per status that has at least one non-archived application, in status order.</param>
public sealed record ApplicationPipeline(IReadOnlyList<PipelineGroup> Groups);

/// <summary>One status column of the pipeline and the applications currently in it, newest activity first.</summary>
/// <param name="Status">The status every application in this group is currently in.</param>
/// <param name="Applications">The applications in this status, ordered by <see cref="PipelineEntry.LastActivityAt"/> descending.</param>
public sealed record PipelineGroup(ApplicationStatus Status, IReadOnlyList<PipelineEntry> Applications);

/// <summary>
/// One application as it appears in the pipeline view, reduced to the card display fields (contract §Pipeline
/// response). <see cref="DaysInStage"/> is computed at read time from when the current stage was entered
/// rather than stored, because it is a presentation concern that would otherwise need keeping current.
/// </summary>
/// <param name="Id">The application id — the subject of the history and status endpoints.</param>
/// <param name="JobId">The job the application tracks.</param>
/// <param name="Title">The job title (raw board text; escaped and truncated by the renderer).</param>
/// <param name="Company">The company display name.</param>
/// <param name="Score">The job's most recent final 0–100 score, or 0 when never scored.</param>
/// <param name="PostingClosed">Whether the underlying posting has closed — shown as a marker, never a status (AC-07).</param>
/// <param name="AppliedAt">When the Owner first applied, or null before <see cref="ApplicationStatus.Applied"/>.</param>
/// <param name="LastActivityAt">The most recent activity — the group's ordering key.</param>
/// <param name="NextActionAt">When a reminder is next due, or null when nothing is being chased.</param>
/// <param name="DaysInStage">Whole days since the current stage was entered, computed at read time.</param>
public sealed record PipelineEntry(
    Guid Id,
    Guid JobId,
    string Title,
    string Company,
    decimal Score,
    bool PostingClosed,
    DateTimeOffset? AppliedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? NextActionAt,
    int DaysInStage);
