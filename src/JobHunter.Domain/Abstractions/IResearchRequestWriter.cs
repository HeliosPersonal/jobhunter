namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write port that queues an on-demand research request (F8 T09, SAD §6.2, AC-05). When the Owner asks
/// about a company whose dossier is stale or absent, the request is queued for the next research cycle rather
/// than run inline — research is a batched, cost-ceilinged operation, not an interactive one — and the
/// command acknowledges "ready with tomorrow's digest". The queue is drained by the daily
/// <c>ResearchTargetSelector</c>, which merges on-demand requests alongside the automatic five without
/// displacing them and never queues the same company twice in a cycle.
///
/// <para>Defined in Domain so the command handler and the API depend on the port, not the table. It carries
/// only the company id and a short reason — <strong>nothing about the Owner's CV</strong> (the CV crosses
/// exactly one boundary, and it is not this one).</para>
/// </summary>
public interface IResearchRequestWriter
{
    /// <summary>
    /// Queues a research request for <paramref name="companyId"/>. Idempotent per company for a pending cycle:
    /// a second request for a company already queued adds nothing. <paramref name="reason"/> is a short,
    /// non-blank note recording why the request was raised (e.g. the on-demand command).
    /// </summary>
    Task EnqueueAsync(Guid companyId, string reason, CancellationToken cancellationToken = default);
}
