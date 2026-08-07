using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Commands;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Formatting;

namespace JobHunter.Telegram.Commands;

/// <summary>
/// <c>/note [text]</c> (contract §Commands, F6 T07): attaches a free-text note to the most-recently-touched
/// application. F6 owns the note-write behaviour; F10 only surfaces it. With text the note is written to the
/// most-recently-active application straight away through the shared <see cref="AddNoteHandler"/> — the one
/// write path the API uses too — and the confirmation <em>names</em> the application it landed on, never a
/// bare "done". With no text the command enters the multi-step flow: it stores a short-lived per-chat
/// <see cref="ConversationState"/> awaiting the note and asks for it, so the Owner's next non-command message
/// resumes the command rather than being treated as one of its own (AC-08). With no application to attach to,
/// it says so rather than failing silently.
///
/// <para>The note body is <strong>never logged</strong> — only its length — because it may contain anything
/// the Owner typed (invariant 12); the confirmation is derived from the application, not the body. No LLM, no
/// CV (the CV crosses exactly one boundary, and it is not this one). The context stored with a pending state
/// carries the target job id, never any content the Owner typed (<see cref="ConversationState"/> holds no
/// argument content).</para>
///
/// <para><strong>Deferred to T10.</strong> The <em>resume</em> half of the flow — a stored state being
/// resumed by the next free-text message, and the last-five pick buttons being routed back — is wired with
/// the dispatch rewire against the full command registry (T10), the same convention T06's <c>/hidden</c>
/// turn-off button follows. This task stores the state and asks; the dispatcher hands the reply back next.</para>
/// </summary>
internal sealed class NoteCommandHandler(
    IApplicationPipelineQuery pipeline,
    AddNoteHandler addNote,
    IConversationStateStore state,
    IClock clock,
    ILogger<NoteCommandHandler> logger) : ICommandHandler
{
    /// <summary>The registry name a pending state carries, so the resume step (T10) knows which command to resume.</summary>
    private const string CommandName = "note";

    /// <summary>The argument the multi-step flow waits for — the note body (SAD §6.2).</summary>
    private const string AwaitingText = "text";

    /// <summary>The context key under which the target application's job id rides — an id, never content.</summary>
    private const string TargetJobKey = "jobId";

    private readonly IApplicationPipelineQuery _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    private readonly AddNoteHandler _addNote = addNote ?? throw new ArgumentNullException(nameof(addNote));
    private readonly IConversationStateStore _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<NoteCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<RenderedMessage>> HandleAsync(
        CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The most-recently-active application is the note's target: the pipeline read is already ordered by
        // last activity within each status, so the single most-recent one across all groups is the head of the
        // flattened, activity-descending list.
        var view = await _pipeline.PipelineAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);
        var target = view.Groups
            .SelectMany(group => group.Applications)
            .OrderByDescending(entry => entry.LastActivityAt)
            .FirstOrDefault();

        if (target is null)
        {
            // Nothing to attach to — say so plainly, never a silent no-op or a write. The last-five pick that
            // lets the Owner choose one is routed with the callback registry (T10).
            _logger.LogDebug("/note requested but no application is being tracked.");
            return [RenderedMessage.PlainText(
                "_" + MarkdownV2Escaper.Escape("You have no application to note yet — apply to or save a role first.") + "_")];
        }

        var body = request.Arguments?.Trim();
        if (string.IsNullOrEmpty(body))
        {
            // No text: enter the multi-step flow. Store the pending state carrying the target job id (an id,
            // never content) and ask for the note; write nothing yet (AC-08).
            var pending = new ConversationState(
                CommandName,
                AwaitingText,
                new Dictionary<string, string> { [TargetJobKey] = target.JobId.ToString() },
                _clock.UtcNow);
            await _state.SetAsync(request.ChatId, pending, cancellationToken).ConfigureAwait(false);

            return [RenderedMessage.PlainText(
                MarkdownV2Escaper.Escape($"Reply with the note for {target.Company} · {target.Title}, or /cancel to stop."))];
        }

        // Inline text: write straight away through the one shared write path and confirm where it landed.
        var outcome = await _addNote
            .Handle(new AddNoteCommand(target.JobId, body, _clock.UtcNow), cancellationToken)
            .ConfigureAwait(false);

        return [RenderedMessage.PlainText(ConfirmationFor(outcome, target))];
    }

    // The reply for each write outcome, named against the application so the Owner sees where the note went
    // (or why it was refused). The body is never echoed — the refusals speak to length or existence, not text.
    private static string ConfirmationFor(AddNoteOutcome outcome, PipelineEntry target) => outcome switch
    {
        AddNoteOutcome.Recorded =>
            MarkdownV2Escaper.Escape($"Noted on {target.Company} · {target.Title}."),
        AddNoteOutcome.TooLong =>
            "_" + MarkdownV2Escaper.Escape($"That note is too long (max {ApplicationNote.MaxLength} characters).") + "_",
        AddNoteOutcome.ApplicationNotFound =>
            "_" + MarkdownV2Escaper.Escape($"No application tracks {target.Company} · {target.Title} anymore.") + "_",
        // A blank body cannot reach here — an empty argument enters the multi-step flow above — but render it
        // as the same helpful line rather than an empty message if it ever does.
        _ => "_" + MarkdownV2Escaper.Escape("There was nothing to note.") + "_",
    };
}
