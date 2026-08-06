using JobHunter.Domain.Common;

namespace JobHunter.Domain.Applications;

/// <summary>
/// One job the Owner has engaged with (F6 [[data-model]] §applications). Created lazily on first action
/// (SAD §4 S2) in <see cref="ApplicationStatus.New"/>, it advances only along permitted transitions
/// ([[adr/0001-permissive-transitions-with-history|ADR-F6-0001]]), recording each as append-only history
/// (QG-1). One application per job — the pipeline is per-opportunity, not per-conversation.
///
/// <para>Two rules are the aggregate's own, not the table's: <c>applied_at</c> is stamped on first entry
/// to <see cref="ApplicationStatus.Applied"/> and never changed afterwards, and a closed posting is
/// recorded as metadata that never touches the status (AC-07) — collapsing the two would fabricate a
/// rejection and poison the evidence F7 learns from.</para>
/// </summary>
public sealed class Application : Entity
{
    private readonly List<StatusTransition> _transitions = [];

    private Application(Guid id, Guid jobId, DateTimeOffset createdAt)
        : base(id)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("An application must reference a job.", nameof(jobId));
        }

        JobId = jobId;
        Status = ApplicationStatus.New;
        CreatedAt = createdAt;
        LastActivityAt = createdAt;
    }

    private Application()
    {
    }

    public Guid JobId { get; private set; }

    public ApplicationStatus Status { get; private set; }

    /// <summary>Set by <see cref="MarkPostingClosed"/>; the status is never changed with it (AC-07).</summary>
    public bool PostingClosed { get; private set; }

    /// <summary>Terminal applications archive after 180 days: hidden from the pipeline, never deleted.</summary>
    public bool Archived { get; private set; }

    /// <summary>Stamped once, on first entry to <see cref="ApplicationStatus.Applied"/>; never changed.</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>When a reminder is next due — a stored column, not a computed value (SAD §4 S6).</summary>
    public DateTimeOffset? NextActionAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The complete, ordered, append-only history (QG-1).</summary>
    public IReadOnlyList<StatusTransition> Transitions => _transitions;

    /// <summary>
    /// Creates an application lazily in <see cref="ApplicationStatus.New"/> with its creating transition
    /// (<see cref="StatusTransition.From"/> is <c>null</c>). The id is supplied by the caller from
    /// <c>IIdGenerator</c>, never database-generated.
    /// </summary>
    public static Application Create(Guid id, Guid jobId, DateTimeOffset now, TransitionSource source)
    {
        var application = new Application(id, jobId, now);
        application._transitions.Add(new StatusTransition(
            id: Guid.NewGuid(),
            applicationId: id,
            from: null,
            to: ApplicationStatus.New,
            source: source,
            occurredAt: now));
        return application;
    }

    /// <summary>
    /// Moves the application to <paramref name="to"/> if <see cref="TransitionRules"/> permits it,
    /// appending a transition, advancing <see cref="LastActivityAt"/>, stamping <see cref="AppliedAt"/> on
    /// first <see cref="ApplicationStatus.Applied"/>, and rescheduling <see cref="NextActionAt"/> from the
    /// <paramref name="policy"/>. A refused move changes nothing and returns the remedy — a failure value,
    /// not an exception, because a refused transition is an expected business outcome (coding-standards §4).
    /// </summary>
    public Result<StatusTransition> ChangeStatus(
        ApplicationStatus to,
        TransitionSource source,
        DateTimeOffset now,
        ReminderPolicy policy,
        string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var evaluation = TransitionRules.Evaluate(Status, to);
        if (!evaluation.IsPermitted)
        {
            return Result<StatusTransition>.Failure(new Error("TransitionNotPermitted", evaluation.Remedy!));
        }

        var from = Status;
        Status = to;
        LastActivityAt = now;

        if (to == ApplicationStatus.Applied && AppliedAt is null)
        {
            AppliedAt = now;
        }

        var threshold = policy.ThresholdFor(to);
        NextActionAt = threshold is null ? null : now.Add(threshold.Value);

        var transition = new StatusTransition(
            id: Guid.NewGuid(),
            applicationId: Id,
            from: from,
            to: to,
            source: source,
            occurredAt: now,
            detail: detail);
        _transitions.Add(transition);

        return Result<StatusTransition>.Success(transition);
    }

    /// <summary>
    /// Records that the underlying posting has closed (from <c>JobClosed</c>, F2). This is metadata: the
    /// status is deliberately not changed (AC-07), because a posting closing tells us nothing about the
    /// Owner's application. Idempotent — re-closing an already-closed posting is a no-op.
    /// </summary>
    public void MarkPostingClosed(DateTimeOffset now)
    {
        if (PostingClosed)
        {
            return;
        }

        PostingClosed = true;
        LastActivityAt = now;
    }
}
