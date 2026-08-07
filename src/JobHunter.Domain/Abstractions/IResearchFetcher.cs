using JobHunter.Domain.Companies;
using JobHunter.Domain.Research;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The port over one company-research category's retrieval (SAD §5). There is exactly one implementation per
/// <see cref="ResearchCategory"/>, so a dead source degrades that one category and never the dossier
/// (SAD §4 S4). A fetcher returns the documents it retrieved as values; an unreachable source, a
/// robots-disallowed path, an SSRF refusal or an empty result all come back as an empty list, never an
/// exception — acquisition of one category is one failure domain (the orchestrator records the category as
/// unavailable, AC-07).
///
/// <para>Every fetch a fetcher makes goes through the shared guarded fetch path (SAD §2, QG-3): the
/// category allowlist and the public-address check are applied to every URL and re-applied after every
/// redirect, so a fetcher cannot reach internal infrastructure whether or not its author read the guard.</para>
/// </summary>
public interface IResearchFetcher
{
    /// <summary>The one category this fetcher retrieves; the orchestrator dispatches by it.</summary>
    ResearchCategory Category { get; }

    /// <summary>
    /// Retrieves the documents this fetcher can find for <paramref name="company"/>. Returns an empty list —
    /// never null, never throwing — when the source is unavailable, refused or empty (AC-07).
    /// </summary>
    Task<IReadOnlyList<FetchedDocument>> FetchAsync(Company company, CancellationToken cancellationToken = default);
}
