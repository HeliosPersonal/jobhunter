using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Research;

namespace JobHunter.Application.Research;

/// <summary>
/// The two outputs a completed dossier hands back to the rest of the pipeline (SAD §6.1). Both are pure
/// functions so the handler that owns the transaction stays a thin edge: it fetches the company, calls these,
/// and saves.
///
/// <para><see cref="ApplyFirmographics"/> is the whitelisted cross-owner write in F8 (AUDIT-RESOLUTION §1):
/// the stage and headcount the model classified from the fetched text are fed back onto the
/// <see cref="Company"/> record so ranking benefits from the better information, not just the dossier (AC-10).
/// The dossier's generation instant is the observation, so <see cref="Company.ApplyFirmographics"/> resolves a
/// disagreement to the newer observation and a re-run of an older dossier never overwrites a fresher one.</para>
/// </summary>
public static class ResearchFeedback
{
    /// <summary>
    /// Feeds the synthesis's optional firmographics back onto <paramref name="company"/>, observed at
    /// <paramref name="generatedAt"/>. Returns whether anything changed — a synthesis that classified neither
    /// field, or one older than what is already recorded, is a no-op (AC-10).
    /// </summary>
    public static bool ApplyFirmographics(Company company, ResearchSynthesis synthesis, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(synthesis);

        return company.ApplyFirmographics(synthesis.Stage, synthesis.EmployeeBand, generatedAt);
    }

    /// <summary>
    /// Mints the <see cref="ResearchCompleted"/> event for <paramref name="dossier"/>, carrying the count of
    /// verified claims it stored. Published once per dossier; the idempotency key is <c>(RunId, CompanyId)</c>.
    /// An empty dossier still completes with a zero count so the digest is never left in silence.
    /// </summary>
    public static ResearchCompleted CompletedEvent(CompanyResearch dossier, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(dossier);

        return new ResearchCompleted(
            dossier.RunId,
            dossier.CompanyId,
            dossier.Id,
            dossier.Claims.Count,
            occurredAt);
    }
}
