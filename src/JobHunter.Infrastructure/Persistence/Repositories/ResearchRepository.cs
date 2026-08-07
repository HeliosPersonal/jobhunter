using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Research;
using JobHunter.Infrastructure.Persistence.Research;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the <see cref="CompanyResearch"/> aggregate and its owned
/// <see cref="ResearchSource"/> and <see cref="ResearchClaim"/> children (F8 data-model §company_research).
/// A dossier goes through EF as one immutable aggregate: its sources and claims are inserted with it in a
/// single <see cref="SaveChangesAsync"/>, and the composite <c>(research_id, source_id)</c> foreign key —
/// deferred to the end of the transaction — lets EF insert claims and sources in any order while still
/// guaranteeing every claim cites a source in its own dossier (invariant 5). There is no update path: a new
/// Run produces a new dossier, never a mutation of an old one.
/// </summary>
public sealed class ResearchRepository(JobHunterDbContext context) : IResearchRepository
{
    public void Add(CompanyResearch research)
    {
        ArgumentNullException.ThrowIfNull(research);
        context.Set<CompanyResearch>().Add(research);

        // categories_covered is derived by the aggregate from its claims (T01), so denormalise the current
        // value into its shadow column at write time — for the Dapper read side (F5 digest, F9 facets). A
        // load never writes it back, so the column can never disagree with what the claims assert.
        context.Entry(research).Property("categories_covered").CurrentValue =
            ResearchCategoryListJson.Serialize(research.CategoriesCovered);
    }

    public Task<CompanyResearch?> FindLatestAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        context.Set<CompanyResearch>()
            .Include(r => r.Sources)
            .Include(r => r.Claims)
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
