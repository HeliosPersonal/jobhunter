using JobHunter.Domain.Research;

namespace JobHunter.Domain.Reporting;

/// <summary>
/// The result of resolving a company by name for the on-demand <c>/company</c> command and the research API
/// (F8 T09, SAD §6.2). A non-null lookup means the company is in the registry; <see cref="LatestDossier"/> is
/// its most recent dossier, or null when it has never been researched. A null lookup — no company by that
/// name — is the "offer to add it" case, kept distinct from "known but never researched" so the command can
/// answer honestly rather than conflating the two.
///
/// <para>It carries only public company facts and its dossier's cited claims — <strong>nothing about the
/// Owner</strong> (the CV crosses exactly one boundary, and it is not this one).</para>
/// </summary>
/// <param name="CompanyId">The resolved company's id — the key an on-demand request is queued against.</param>
/// <param name="DisplayName">The company's display name, echoed back to the Owner.</param>
/// <param name="LatestDossier">The most recent dossier, or null when the company has never been researched.</param>
public sealed record CompanyResearchLookup(
    Guid CompanyId,
    string DisplayName,
    ResearchDossierSnapshot? LatestDossier);

/// <summary>
/// A read-model projection of one <see cref="CompanyResearch"/> dossier — the summary, when it was generated
/// (so its age can be judged and shown), its cited claims and the categories that produced nothing (AC-07).
/// It mirrors the aggregate's own order — warnings first — because the query reads the stored rows the
/// aggregate wrote in that order. Presentation-facing and CV-free.
/// </summary>
/// <param name="Summary">The two-or-three-sentence summary, itself constrained to the cited claims.</param>
/// <param name="GeneratedAt">When the dossier was produced — shown as its age and used to judge freshness.</param>
/// <param name="Claims">The cited claims, warnings first, each with its source URL and observed date.</param>
/// <param name="CategoriesUnavailable">The categories that produced nothing, so absence is visible (AC-07).</param>
public sealed record ResearchDossierSnapshot(
    string Summary,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ResearchClaimFacts> Claims,
    IReadOnlyList<ResearchCategory> CategoriesUnavailable);

/// <summary>
/// One cited claim as the read side returns it (F8 T09): its category, the one-sentence claim, the date it
/// was observed (copied from its source, AC-03), the exact source URL it links to (AC-02), and whether it is
/// a warning surfaced first (AC-04). Every string is raw third-party text — the formatter escapes each one.
/// </summary>
/// <param name="Category">The claim's category.</param>
/// <param name="Claim">The one-sentence claim.</param>
/// <param name="ObservedAt">The date the source was observed (AC-03).</param>
/// <param name="SourceUrl">The exact fetched URL the claim cites (AC-02).</param>
/// <param name="IsWarning">Whether this is a warning category, surfaced first (AC-04).</param>
public sealed record ResearchClaimFacts(
    ResearchCategory Category,
    string Claim,
    DateTimeOffset ObservedAt,
    string SourceUrl,
    bool IsWarning);
