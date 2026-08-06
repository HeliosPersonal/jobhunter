namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// The Owner acted on a delivered digest card — a status tap resolved to its job and instant (event-catalog
/// §3, F5 SAD §6.1, F6 SAD §6.1). Published by <c>JobHunter.Telegram</c> and consumed by F6's owner-action
/// handler, which creates the application lazily on the first action (SAD §4 S2) and advances it. It carries
/// only the job, the chosen action, the chat and the instant — nothing about the Owner beyond the chat id
/// (F4 invariant), and never the card body.
///
/// <para>Idempotency key is <c>(JobId, Action, OccurredAt)</c>: a redelivered tap carries the same key, so
/// the inbox collapses it and the handler appends no second transition. <see cref="Applied"/> is the one
/// action minted only here — it is the Owner recording that they applied, never the system applying for them
/// (invariant 7).</para>
/// </summary>
public sealed record OwnerActionRecorded(
    Guid JobId,
    string Action,
    long ChatId,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    /// <summary>The Owner opened the apply link — a URL button with no pipeline effect; the application is untouched.</summary>
    public const string Open = "Open";

    /// <summary>The Owner dismissed the card — the application moves to <c>Ignored</c>.</summary>
    public const string Ignore = "Ignore";

    /// <summary>The Owner kept the card for later — the application moves to <c>Saved</c>.</summary>
    public const string Save = "Save";

    /// <summary>The Owner recorded that they applied — the application moves to <c>Applied</c> (never applied for them, invariant 7).</summary>
    public const string Applied = "Applied";
}

/// <summary>
/// An application moved from one status to another (event-catalog §3, F6 SAD §6.1). Published inside the same
/// transaction as the transition it describes (AC-03), so a consumer reacting to it always finds the recorded
/// history. Consumed by F7 (signal capture, weighted by the outcome) and F9 (search-index update). It carries
/// the application and job ids and the from/to statuses as text — nothing about the Owner (F4 invariant).
///
/// <para>Idempotency key is <c>(ApplicationId, ToStatus, OccurredAt)</c> (SAD §8): a redelivered action that
/// appended no second transition also re-emits the same key, so a downstream consumer collapses the
/// duplicate.</para>
/// </summary>
public sealed record ApplicationStatusChanged(
    Guid ApplicationId,
    Guid JobId,
    string FromStatus,
    string ToStatus,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
