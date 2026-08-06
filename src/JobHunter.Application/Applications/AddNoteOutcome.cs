namespace JobHunter.Application.Applications;

/// <summary>
/// The result of an <see cref="AddNoteCommand"/> — a value, not an exception, because each case is an
/// expected business outcome the caller renders as a clear message (coding-standards §4). The refusals are
/// distinct so the Telegram <c>/note</c> reply and the API status code can differ per case.
/// </summary>
public enum AddNoteOutcome
{
    /// <summary>The note was stored and counts as activity (AC-06).</summary>
    Recorded,

    /// <summary>The body was blank or whitespace — nothing to record.</summary>
    Empty,

    /// <summary>The body exceeded <see cref="Domain.Applications.ApplicationNote.MaxLength"/> characters.</summary>
    TooLong,

    /// <summary>No application tracks the job yet — a note attaches to an application, it does not create one.</summary>
    ApplicationNotFound,
}
