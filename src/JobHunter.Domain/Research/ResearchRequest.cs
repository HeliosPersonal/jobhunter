using JobHunter.Domain.Common;

namespace JobHunter.Domain.Research;

/// <summary>
/// One on-demand research request queued for the next cycle (F8 T09, SAD §6.2, AC-05). When the Owner asks
/// about a company through <c>/company</c> and its dossier is stale or absent, the request is queued here
/// rather than run inline — research is batched and cost-ceilinged, never interactive — and the next
/// <c>ResearchTargetSelector</c> drains the pending ids alongside the automatic five without displacing them.
///
/// <para>Identity is the surrogate <see cref="Entity.Id"/>, but the queue is deduplicated on
/// <c>(company_id) WHERE NOT consumed</c>: asking about the same company twice before a cycle drains the queue
/// does not enqueue it twice. <see cref="Consumed"/> is the only mutable field — the cycle flips it once it
/// has taken the company into scope, so a drained request is not researched forever. The row carries a short
/// <see cref="Reason"/> for attribution and <strong>nothing about the Owner's CV</strong> (the CV crosses
/// exactly one boundary, and it is not this one).</para>
/// </summary>
public sealed class ResearchRequest : Entity
{
    /// <summary>
    /// Queues a research request for <paramref name="companyId"/>. <paramref name="reason"/> is a short,
    /// non-blank note recording why the request was raised (e.g. the on-demand command).
    /// </summary>
    public ResearchRequest(
        Guid id,
        Guid companyId,
        string reason,
        DateTimeOffset requestedAt)
        : base(id)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A research request must reference a Company.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        CompanyId = companyId;
        Reason = reason.Trim();
        RequestedAt = requestedAt;
        Consumed = false;
    }

    private ResearchRequest()
    {
    }

    public Guid CompanyId { get; private init; }

    /// <summary>A short note recording why the request was raised, for attribution when the cycle drains it.</summary>
    public string Reason { get; private init; } = null!;

    public DateTimeOffset RequestedAt { get; private init; }

    /// <summary>True once the cycle has taken this company into scope; drained requests are not re-run.</summary>
    public bool Consumed { get; private set; }

    /// <summary>Marks this request drained after the cycle that researches it has taken it into scope.</summary>
    public void MarkConsumed() => Consumed = true;
}
