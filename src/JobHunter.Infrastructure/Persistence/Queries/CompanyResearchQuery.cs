using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Research;
using JobHunter.Infrastructure.Persistence.Research;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the on-demand <c>/company</c> command and the research API (F8 T09 C3, SAD §6.2).
/// Implements <see cref="ICompanyResearchQuery"/> with Dapper, read-only (architecture rule 4 forbids a write
/// here): it resolves a company by <c>display_name</c> — case-insensitively, since the Owner types the name —
/// and loads its latest dossier, the newest by <c>generated_at</c> served by <c>idx_research_company_latest</c>.
///
/// <para>Every claim is joined back to its cited <c>research_sources</c> row through the dossier-scoped
/// <c>(research_id, source_id)</c> key so it carries the exact URL invariant 5 requires, and warnings are
/// returned first (<c>ORDER BY is_warning DESC, category</c>, AC-04). <c>categories_unavailable</c> is read
/// from its <c>jsonb</c> column so the caller can state which categories produced nothing (AC-07). A null
/// company resolution is kept distinct from a resolved company with no dossier. It selects
/// <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, and it is not this one
/// (F4 invariant).</para>
/// </summary>
public sealed class CompanyResearchQuery(INpgsqlConnectionFactory connectionFactory) : ICompanyResearchQuery
{
    private const string CompanySql =
        """
        SELECT id            AS CompanyId,
               display_name  AS DisplayName
        FROM companies
        WHERE LOWER(display_name) = LOWER(@Name)
        LIMIT 1
        """;

    private const string LatestDossierSql =
        """
        SELECT id                     AS ResearchId,
               summary                AS Summary,
               generated_at           AS GeneratedAt,
               categories_unavailable AS CategoriesUnavailable
        FROM company_research
        WHERE company_id = @CompanyId
        ORDER BY generated_at DESC
        LIMIT 1
        """;

    private const string ClaimsSql =
        """
        SELECT cl.category    AS Category,
               cl.claim       AS Claim,
               cl.observed_at AS ObservedAt,
               cl.is_warning  AS IsWarning,
               s.url          AS SourceUrl
        FROM research_claims cl
        JOIN research_sources s
          ON s.research_id = cl.research_id AND s.id = cl.source_id
        WHERE cl.research_id = @ResearchId
        ORDER BY cl.is_warning DESC, cl.category
        """;

    public async Task<CompanyResearchLookup?> ResolveByNameAsync(
        string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var companyCommand = new CommandDefinition(
            CompanySql, new { Name = name.Trim() }, cancellationToken: cancellationToken);
        var company = await connection.QuerySingleOrDefaultAsync<CompanyRow>(companyCommand);
        if (company is null)
        {
            return null;
        }

        var dossier = await LoadLatestDossierAsync(connection, company.CompanyId, cancellationToken);
        return new CompanyResearchLookup(company.CompanyId, company.DisplayName, dossier);
    }

    public async Task<ResearchDossierSnapshot?> LatestForCompanyAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await LoadLatestDossierAsync(connection, companyId, cancellationToken);
    }

    private static async Task<ResearchDossierSnapshot?> LoadLatestDossierAsync(
        System.Data.Common.DbConnection connection, Guid companyId, CancellationToken cancellationToken)
    {
        var dossierCommand = new CommandDefinition(
            LatestDossierSql, new { CompanyId = companyId }, cancellationToken: cancellationToken);
        var dossier = await connection.QuerySingleOrDefaultAsync<DossierRow>(dossierCommand);
        if (dossier is null)
        {
            return null;
        }

        var claimsCommand = new CommandDefinition(
            ClaimsSql, new { dossier.ResearchId }, cancellationToken: cancellationToken);
        var claims = await connection.QueryAsync<ClaimRow>(claimsCommand);

        var facts = claims
            .Select(c => new ResearchClaimFacts(
                Enum.Parse<ResearchCategory>(c.Category), c.Claim, c.ObservedAt, c.SourceUrl, c.IsWarning))
            .ToList();

        return new ResearchDossierSnapshot(
            dossier.Summary,
            dossier.GeneratedAt,
            facts,
            ResearchCategoryListJson.Deserialize(dossier.CategoriesUnavailable));
    }

    private sealed record CompanyRow(Guid CompanyId, string DisplayName);

    private sealed record DossierRow(
        Guid ResearchId, string Summary, DateTimeOffset GeneratedAt, string CategoriesUnavailable);

    private sealed record ClaimRow(
        string Category, string Claim, DateTimeOffset ObservedAt, bool IsWarning, string SourceUrl);
}
