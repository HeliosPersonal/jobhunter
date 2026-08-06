namespace JobHunter.Application.Applications;

/// <summary>
/// A request to attach a free-text note to the application tracking a job (F6 T07, AC-06). Keyed by
/// <see cref="JobId"/> — the same job-scoped write path every F6 handler uses, so it fits the repository's
/// pinned surface (QG-1); the API, which addresses an application by id, resolves the id to its job before
/// dispatching. <see cref="Body"/> is the raw text the Owner typed and is never logged (invariant 12).
/// </summary>
/// <param name="JobId">The job whose application the note is attached to.</param>
/// <param name="Body">The note text, exactly as the Owner typed it (validated by the handler, never logged).</param>
/// <param name="CreatedAt">When the note was written (from <c>IClock</c>, never <c>DateTime.Now</c>).</param>
public sealed record AddNoteCommand(Guid JobId, string Body, DateTimeOffset CreatedAt);
