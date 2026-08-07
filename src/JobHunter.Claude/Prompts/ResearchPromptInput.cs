using JobHunter.Domain.Research;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The facts one research-synthesis prompt is rendered from (research-schema §Prompt, User template). It is
/// a projection of what the fetchers found for one company — its display name, canonical domain, the
/// documents retrieved (each tagged with the category whose fetcher found it), and the categories that
/// yielded <em>nothing</em> — and carries nothing about the Owner: a research prompt describes the company,
/// not the fit, so the CV never enters it (invariant — the CV crosses exactly one boundary, and it is F4's,
/// not this one).
///
/// <para><see cref="EmptyCategories"/> is rendered explicitly in the prompt on purpose: telling the model
/// the absence is known and does not need filling measurably reduces the temptation to supply a claim from
/// memory (research-schema §Prompt note).</para>
/// </summary>
public sealed record ResearchPromptInput(
    string DisplayName,
    string CanonicalDomain,
    IReadOnlyList<CategorisedDocument> Documents,
    IReadOnlyList<ResearchCategory> EmptyCategories);

/// <summary>
/// One fetched document tagged with the category whose fetcher retrieved it. The category is a property of
/// the fetch, not of the document text, so it is carried here rather than on <see cref="FetchedDocument"/>;
/// it appears in the prompt as the <c>category:</c> header the model echoes back on each claim.
/// </summary>
public sealed record CategorisedDocument(ResearchCategory Category, FetchedDocument Document);
