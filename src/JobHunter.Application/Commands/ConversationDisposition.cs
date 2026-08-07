namespace JobHunter.Application.Commands;

/// <summary>
/// What the dispatcher should do about the conversation at the head of a turn (SAD §6.2, AC-08). The
/// resolver decides this once, before resolving the message as a command, so the state machine lives in
/// one pure place.
/// </summary>
public enum ConversationDisposition
{
    /// <summary>Fail-closed default; never a resolved disposition.</summary>
    Unspecified = 0,

    /// <summary>Nothing pending (or the pending one just ended) — treat the message as a fresh command.</summary>
    Proceed = 1,

    /// <summary>A live pending command; a non-command message resumes it with that message as input.</summary>
    Resume = 2,

    /// <summary>A live pending command is replaced by a new command; the Owner is told it was dropped.</summary>
    Superseded = 3,

    /// <summary>The Owner sent <c>/cancel</c> and a command was pending; it is abandoned.</summary>
    Cancelled = 4,

    /// <summary>The Owner sent <c>/cancel</c> with nothing pending; a cheerful no-op, never an error.</summary>
    NothingToCancel = 5,

    /// <summary>The pending command passed its lifetime; the Owner is told it expired and re-issues.</summary>
    Expired = 6,
}
