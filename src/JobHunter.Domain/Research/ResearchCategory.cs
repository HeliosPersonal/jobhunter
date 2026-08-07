namespace JobHunter.Domain.Research;

/// <summary>
/// The eight company-research categories (SAD §8). Each has exactly one fetcher behind
/// <c>IResearchFetcher</c>, so a dead source degrades one category and never the dossier (SAD §4 S4). The
/// set is closed: a ninth category is a deliberate schema change, guarded by a test that locks the count.
/// Persisted as <c>text</c>, never an ordinal (coding-standards §5).
///
/// <para><see cref="News"/> and <see cref="Layoffs"/> are the two whose value evaporates fastest, so they
/// carry a shorter freshness threshold than the rest (see <see cref="Freshness"/>).</para>
/// </summary>
public enum ResearchCategory
{
    /// <summary>Funding rounds and investors, from sources with a public API or feed.</summary>
    Funding,

    /// <summary>The company's engineering blog — evidence of an engineering culture that writes.</summary>
    EngineeringBlog,

    /// <summary>Public open-source presence, from the GitHub organisation API.</summary>
    OpenSource,

    /// <summary>Employer reviews, only from sources with a usable public API or feed (O4).</summary>
    Reviews,

    /// <summary>Recent news, from a public search feed. Refreshes at seven days.</summary>
    News,

    /// <summary>Layoffs and workforce reductions — a warning category. Refreshes at seven days.</summary>
    Layoffs,

    /// <summary>The company's technology stack, inferred from public evidence.</summary>
    Stack,

    /// <summary>The interview process, from public candidate-facing descriptions.</summary>
    InterviewProcess,
}
