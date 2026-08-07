using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the on-demand <c>/company</c> command and the research API (F8 T09, SAD §6.2). It
/// resolves a company by the name the Owner typed and, when the company is known, returns its most recent
/// dossier alongside its identity. Read-only (Dapper, architecture rule 4); defined in Domain so the command
/// handler and the API depend on the port, not the SQL.
///
/// <para>A null result means no company by that name is in the registry — kept distinct from a known company
/// with no dossier — so the caller can offer to add it rather than silently failing. It selects
/// <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, and it is not this
/// one (F4 invariant).</para>
/// </summary>
public interface ICompanyResearchQuery
{
    /// <summary>
    /// Resolves <paramref name="name"/> to a company and its latest dossier, or null when no company by that
    /// name is in the registry.
    /// </summary>
    Task<CompanyResearchLookup?> ResolveByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// The latest dossier for a company already identified by <paramref name="companyId"/>, or null when the
    /// company has never been researched. Backs the API's dossier-by-id read.
    /// </summary>
    Task<ResearchDossierSnapshot?> LatestForCompanyAsync(
        Guid companyId, CancellationToken cancellationToken = default);
}
