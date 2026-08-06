namespace JobHunter.Domain.Applications;

/// <summary>
/// One application the reminder sweep found due (F6 SAD §6.2, T06): its <c>next_action_at</c> has passed and
/// it is not archived, so it is the indexed read behind <c>idx_applications_due</c> — never a scan (done-when
/// 5). It carries just enough to name the application and suggest an action in one message: the job title and
/// company, the current status, and whether the posting has closed (which decides whether the nudge is
/// "chase this stale stage" or "the posting closed — drop it or apply elsewhere", test-plan Saved + closed).
///
/// <para>It also carries <see cref="LastReminderCondition"/> so the sweep can suppress a repeat: one reminder
/// per <c>(application, condition)</c> until the condition clears or recurs (QG-3). It selects
/// <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is not this one
/// (F4 invariant).</para>
/// </summary>
/// <param name="ApplicationId">The application due for a nudge — the subject of the reminder.</param>
/// <param name="JobId">The job it tracks, for the apply-url the nudge links to.</param>
/// <param name="Title">The job title (raw board text; escaped and truncated by the renderer).</param>
/// <param name="Company">The company display name.</param>
/// <param name="ApplyUrl">The posting's apply-url — the "open posting" link a still-open nudge offers.</param>
/// <param name="Status">The status the application is currently in.</param>
/// <param name="PostingClosed">Whether the posting has closed — decides the suggested action.</param>
/// <param name="LastReminderCondition">The condition the last reminder fired for, or null — the suppression key.</param>
public sealed record DueReminder(
    Guid ApplicationId,
    Guid JobId,
    string Title,
    string Company,
    string ApplyUrl,
    ApplicationStatus Status,
    bool PostingClosed,
    string? LastReminderCondition)
{
    /// <summary>
    /// The condition a reminder would fire for right now — the same rule the aggregate applies
    /// (<see cref="Application.CurrentReminderCondition"/>): <see cref="Application.PostingClosedCondition"/>
    /// when the posting has closed, otherwise <c>stale:{Status}</c>. Kept here too so the sweep can decide
    /// suppression from the read model without loading the aggregate first.
    /// </summary>
    public string CurrentCondition() =>
        PostingClosed ? Application.PostingClosedCondition : $"stale:{Status}";

    /// <summary>
    /// Whether a reminder for this application is already suppressed: the last one fired for the same
    /// condition, so no new one is due until the condition clears or recurs (QG-3, done-when 1).
    /// </summary>
    public bool IsAlreadyReminded() =>
        string.Equals(LastReminderCondition, CurrentCondition(), StringComparison.Ordinal);
}
