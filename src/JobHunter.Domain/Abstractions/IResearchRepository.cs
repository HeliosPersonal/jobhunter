using JobHunter.Domain.Research;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write port over the <see cref="CompanyResearch"/> aggregate and its owned
/// <see cref="ResearchSource"/> and <see cref="ResearchClaim"/> children (F8 data-model §company_research).
/// A dossier is written whole in one transaction — its fetched sources first, then the cited claims that
/// rest on them — so the "every claim carries a source" invariant the aggregate guarantees at construction
/// is preserved all the way to the row. One dossier per <c>(company, run)</c> is a database constraint
/// (<c>uq_research_company_run</c>), and a claim citing a source from another dossier is rejected by the
/// composite foreign key, so invariant 5 survives even a hand-written insert.
/// </summary>
public interface IResearchRepository
{
    /// <summary>Stages a dossier, its sources and its claims for insert in one transaction.</summary>
    void Add(CompanyResearch research);

    /// <summary>
    /// The most recent dossier for <paramref name="companyId"/> with its sources and claims, or null when the
    /// company has never been researched. Newest-first by <c>generated_at</c>, served by
    /// <c>idx_research_company_latest</c> — the read a freshness check turns on (F8 T05).
    /// </summary>
    Task<CompanyResearch?> FindLatestAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>Commits the staged changes in one transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
