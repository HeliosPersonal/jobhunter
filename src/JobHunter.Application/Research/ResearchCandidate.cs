using JobHunter.Domain.Research;

namespace JobHunter.Application.Research;

/// <summary>
/// One company standing behind a Run's top jobs, with the score that ranks it for research and — if it has
/// been researched before — the freshness of its latest dossier (SAD §6.1). The Application layer projects
/// this from <c>companies</c> and the latest <c>company_research</c> row (T08); the selector treats it as a
/// pure value, so target selection is deterministic and testable without a database.
/// </summary>
public sealed record ResearchCandidate(
    Guid CompanyId,
    double Score,
    DossierFreshness? LatestDossier);

/// <summary>
/// What target selection needs to know about a company's latest dossier: when it was generated and which
/// categories it covered. Staleness composes the domain <see cref="Freshness"/> policy across those
/// categories, so a dossier that surfaced a volatile category (<see cref="ResearchCategory.News"/> or
/// <see cref="ResearchCategory.Layoffs"/>) ages out sooner than one that did not. A dossier covering nothing
/// is not volatile and ages at the default window.
/// </summary>
public sealed record DossierFreshness(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ResearchCategory> CategoriesCovered);
