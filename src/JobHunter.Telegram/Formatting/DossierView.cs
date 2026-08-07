namespace JobHunter.Telegram.Formatting;

/// <summary>
/// The display projection of one <see cref="Domain.Research.CompanyResearch"/> dossier — everything the
/// <see cref="DossierFormatter"/> needs to render it and nothing else (F8 T09, research-schema contract).
/// The domain aggregate already orders warnings first and derives its covered/unavailable categories from
/// the claims (T01); this view carries those decisions across the layer boundary as plain strings so the
/// formatter stays a pure function of what it is handed. It holds <strong>nothing about the Owner</strong> —
/// only public company facts and their sources (the CV crosses exactly one boundary, and it is not this one).
/// </summary>
/// <param name="CompanyName">The company's display name.</param>
/// <param name="Summary">The two-or-three-sentence summary, itself constrained to the cited claims.</param>
/// <param name="GeneratedAt">When the dossier was produced — shown so the Owner can judge its age.</param>
/// <param name="Claims">The cited claims, warnings first (the aggregate's order), each with its source.</param>
/// <param name="CategoriesUnavailable">The categories that produced nothing, stated so absence is visible (AC-07).</param>
public sealed record DossierView(
    string CompanyName,
    string Summary,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DossierClaim> Claims,
    IReadOnlyList<string> CategoriesUnavailable);

/// <summary>
/// One rendered claim (F8 research-schema contract): the category it belongs to, the one-sentence claim, the
/// date it was observed (AC-03, copied from its source), the exact source URL it links to (AC-02), and
/// whether it is a warning (layoffs, funding difficulty) surfaced first (AC-04). Every string is raw,
/// untrusted third-party text — the formatter escapes each one.
/// </summary>
/// <param name="Category">The claim's category, e.g. <c>Funding</c>.</param>
/// <param name="Claim">The one-sentence claim.</param>
/// <param name="ObservedAt">The date the source was observed — shown alongside the claim (AC-03).</param>
/// <param name="SourceUrl">The exact fetched URL the claim links to (AC-02).</param>
/// <param name="IsWarning">Whether this is a warning category, surfaced ahead of the rest (AC-04).</param>
public sealed record DossierClaim(
    string Category,
    string Claim,
    DateTimeOffset ObservedAt,
    string SourceUrl,
    bool IsWarning);
