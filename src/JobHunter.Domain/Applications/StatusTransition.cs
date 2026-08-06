using JobHunter.Domain.Common;

namespace JobHunter.Domain.Applications;

/// <summary>
/// One recorded step in an <see cref="Application"/>'s history (F6 [[data-model]] §application_transitions).
/// The history is <b>append-only</b> — there is no update or delete path (QG-1) — so a correction appears
/// as a new transition, which is what makes the history trustworthy rather than merely present.
///
/// <para><see cref="From"/> is <c>null</c> for the creating transition into
/// <see cref="ApplicationStatus.New"/>; <see cref="Detail"/> is an optional note such as
/// <c>reminder actioned</c>.</para>
/// </summary>
public sealed class StatusTransition : Entity
{
    public StatusTransition(
        Guid id,
        Guid applicationId,
        ApplicationStatus? from,
        ApplicationStatus to,
        TransitionSource source,
        DateTimeOffset occurredAt,
        string? detail = null)
        : base(id)
    {
        if (applicationId == Guid.Empty)
        {
            throw new ArgumentException("A transition must reference an application.", nameof(applicationId));
        }

        ApplicationId = applicationId;
        From = from;
        To = to;
        Source = source;
        OccurredAt = occurredAt;
        Detail = detail;
    }

    private StatusTransition()
    {
    }

    public Guid ApplicationId { get; private set; }

    /// <summary>The status moved from; <c>null</c> for the creating transition.</summary>
    public ApplicationStatus? From { get; private set; }

    public ApplicationStatus To { get; private set; }

    public TransitionSource Source { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string? Detail { get; private set; }
}
