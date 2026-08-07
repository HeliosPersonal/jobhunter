using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Research;

namespace JobHunter.Application.Research;

/// <summary>
/// Everything the assembler needs to turn one company's research cycle into a <see cref="CompanyResearch"/>
/// dossier (SAD §6.1): the documents the fetchers retrieved, the synthesiser's parsed output, and the
/// identity/stamping the handler owns (the run it belongs to, the prompt version, the generation instant).
/// The assembler is a pure function of this — no clock, no HTTP, no Anthropic type — so the warnings order,
/// the discard count and the unavailable-category set are all deterministic under test.
/// </summary>
public sealed record ResearchDossierInput(
    Guid CompanyId,
    Guid RunId,
    IReadOnlyList<FetchedCategoryDocument> Documents,
    ResearchSynthesis Synthesis,
    string PromptVersion,
    DateTimeOffset GeneratedAt);

/// <summary>
/// One document a fetcher retrieved, tagged with the category whose fetcher found it (mirrors the Claude
/// layer's <c>CategorisedDocument</c>, but owned here so the Application layer depends on its own type). Every
/// one becomes a stored <see cref="ResearchSource"/> before verification, so it is the citation authority.
/// </summary>
public sealed record FetchedCategoryDocument(ResearchCategory Category, FetchedDocument Document);

/// <summary>
/// The synthesiser's parsed output, before verification (research-schema §Output record): the summary, the
/// asserted-but-untrusted claims, and the optional firmographics the model classified from the fetched text.
/// The claims carry bare URL strings — a <see cref="ResearchClaim"/> cannot be constructed until the URL is
/// proven fetched — and <see cref="Stage"/> / <see cref="EmployeeBand"/> are null when the documents gave no
/// evidence (AC-10), never guessed.
/// </summary>
public sealed record ResearchSynthesis(
    string Summary,
    IReadOnlyList<UnverifiedClaim> Claims,
    CompanyStage? Stage = null,
    string? EmployeeBand = null);
