namespace JobHunter.Application.Actions;

/// <summary>
/// The four actions a digest card offers, in the fixed button order (F5
/// [[../contracts/telegram-messages|contract]] §Card). Muscle memory matters more than context-sensitivity,
/// so every card carries all four in this order. Their durable consequences differ, which is the whole point
/// of routing them through <see cref="RecordCardActionHandler"/> rather than treating them alike:
/// <see cref="Open"/> is a URL button that opens the posting directly (no callback fires, nothing recorded);
/// <see cref="Ignore"/> and <see cref="Save"/> are card-action signals F5 captures itself; and
/// <see cref="Applied"/> is an F6 outcome whose durable record needs an application id F5 does not have.
/// </summary>
public enum CardAction
{
    /// <summary>Open the apply link — a URL button, so no callback reaches the handler.</summary>
    Open,

    /// <summary>Dismiss the card — captured as an <c>Ignored</c> signal (D7: "Won't show similar").</summary>
    Ignore,

    /// <summary>Save the card for later — captured as a <c>Saved</c> signal.</summary>
    Save,

    /// <summary>Mark the job applied — an F6 outcome, recorded through <c>OwnerActionRecorded</c>, not as an F5 signal.</summary>
    Applied,

    /// <summary>
    /// Rate the card "worth opening" — captured as a <c>Rated</c> signal (F4 T20, the weekly precision@10 loop).
    /// Only the affirmative choice is a <see cref="Rate"/>; "not worth opening" carries a no-op token and records
    /// nothing, so the presence of a <c>Rated</c> signal <em>is</em> the positive judgement precision@10 counts.
    /// </summary>
    Rate,
}

/// <summary>
/// What <see cref="RecordCardActionHandler"/> did with a tap, so the Telegram layer can acknowledge and
/// update the keyboard without re-deriving the action's meaning.
/// </summary>
public enum CardActionOutcome
{
    /// <summary>A card-action signal was captured on this call (the first tap).</summary>
    Captured,

    /// <summary>An identical signal was already present — idempotent no-op, still acknowledged.</summary>
    AlreadyCaptured,

    /// <summary>The job no longer exists or has closed; nothing invalid was recorded (AC-09).</summary>
    JobUnavailable,

    /// <summary>The action's durable record belongs to another context (Open opens directly; Applied is F6).</summary>
    RecordedElsewhere,
}

/// <summary>
/// A single Owner tap on a delivered card, resolved to its job and instant (F5 T10, AC-03/AC-08). The
/// handler turns it into durable evidence in the same step it is applied — capture is never a separate
/// action that can fail on its own.
/// </summary>
/// <param name="JobId">The job the tapped card presents.</param>
/// <param name="Action">Which of the four buttons the Owner tapped.</param>
/// <param name="OccurredAt">The instant of the tap — the signal's <c>occurred_at</c>, and its idempotence key.</param>
public sealed record RecordCardActionCommand(Guid JobId, CardAction Action, DateTimeOffset OccurredAt);
