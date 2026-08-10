namespace JobHunter.Telegram.Commands;

/// <summary>
/// One resumed step of a multi-step command (T10, SAD §6.2): the Owner's chat, what the pending command was
/// <see cref="Awaiting"/>, the structured <see cref="Context"/> it stored to resume with (an application id,
/// a parsed amount — never content the Owner typed), and the <see cref="Input"/> that resumes it, which is
/// the incoming message verbatim. It is the resume-half twin of <see cref="CommandRequest"/>: the same chat,
/// but carrying the pending state rather than a freshly parsed argument tail.
/// </summary>
/// <param name="ChatId">The Owner's chat, the reply target and the key the handler clears its state under.</param>
/// <param name="Awaiting">What the pending command was waiting for, e.g. <c>text</c> or <c>confirm</c>.</param>
/// <param name="Context">The structured values the command stored to resume — never Owner-typed content.</param>
/// <param name="Input">The resume input: the incoming message verbatim (a note body, or <c>confirm</c>).</param>
public sealed record CommandResumeRequest(
    long ChatId,
    string Awaiting,
    IReadOnlyDictionary<string, string> Context,
    string Input);
