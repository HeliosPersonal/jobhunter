using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Applications;

/// <summary>
/// Attaches a free-text note to the application tracking a job (F6 T07, AC-06) — the one write path behind
/// both the Telegram <c>/note</c> command and the API <c>POST …/notes</c>. It loads the application by job
/// through the repository's only read (<see cref="IApplicationRepository.FindByJobAsync"/>, QG-1), validates
/// the body at the boundary and, if it passes, appends the note through the aggregate and commits. A note is
/// activity (it advances <c>last_activity_at</c>, so it defers the reminder sweep — done-when 4) but never a
/// status change.
///
/// <para>Every case is a value, not an exception (coding-standards §4): a blank body, an over-long body and a
/// note for an untracked job each return a distinct <see cref="AddNoteOutcome"/> the caller renders. The
/// length is checked here rather than caught from the aggregate so the refusal is an outcome, not a thrown
/// <see cref="ArgumentException"/>. Nothing is written on a refusal.</para>
///
/// <para>The note body is <strong>never logged</strong> — only its length — because it may contain anything
/// the Owner typed (invariant 12, done-when 3). No CV, no secret, no free text reaches a log line or a span.</para>
/// </summary>
public sealed class AddNoteHandler(
    IApplicationRepository applications,
    IIdGenerator ids,
    ILogger<AddNoteHandler> logger)
{
    private readonly IApplicationRepository _applications = applications ?? throw new ArgumentNullException(nameof(applications));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly ILogger<AddNoteHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AddNoteOutcome> Handle(AddNoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Validate at the boundary as values (coding-standards §4): a blank or over-long body is a refusal the
        // caller renders, never a thrown ArgumentException from the aggregate. Log the length, never the body.
        if (string.IsNullOrWhiteSpace(command.Body))
        {
            _logger.LogInformation("Note for job {JobId} refused: blank body.", command.JobId);
            return AddNoteOutcome.Empty;
        }

        if (command.Body.Length > ApplicationNote.MaxLength)
        {
            _logger.LogInformation(
                "Note for job {JobId} refused: {Length} characters exceeds the {MaxLength} cap.",
                command.JobId, command.Body.Length, ApplicationNote.MaxLength);
            return AddNoteOutcome.TooLong;
        }

        var application = await _applications.FindByJobAsync(command.JobId, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            // A note annotates an existing application; it does not lazily create one (unlike an owner action).
            _logger.LogInformation("Note for job {JobId} refused: no application tracks it yet.", command.JobId);
            return AddNoteOutcome.ApplicationNotFound;
        }

        application.AddNote(_ids.NewId(), command.Body, command.CreatedAt);
        await _applications.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Recorded a {Length}-character note on application {ApplicationId} for job {JobId}.",
            command.Body.Length, application.Id, command.JobId);

        return AddNoteOutcome.Recorded;
    }
}
