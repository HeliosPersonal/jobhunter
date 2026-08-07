using System.Collections.ObjectModel;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Research;

/// <summary>
/// One research dossier per (company, run) (data-model §company_research). It carries only cited claims:
/// every claim it holds rests on one of its own recorded <see cref="Sources"/>, checked at construction,
/// so the dossier as a whole cannot contain a claim the model invented (SAD §6.1 — the verification loop
/// is the feature). It states which categories produced nothing (<see cref="CategoriesUnavailable"/>),
/// because absence of information is information (AC-07), and orders warnings ahead of the rest so
/// layoffs and funding difficulty surface first (AC-04).
///
/// <para>The aggregate depends on no HTTP, EF Core or Anthropic type (T01 done-when 5): it is assembled
/// from values the Application layer has already fetched, verified and discarded. <see cref="CategoriesCovered"/>
/// is derived from the claims rather than stored, so it can never disagree with what was actually asserted.</para>
/// </summary>
public sealed class CompanyResearch : Entity
{
    private readonly List<ResearchSource> _sources = [];
    private readonly List<ResearchClaim> _claims = [];
    private readonly List<ResearchCategory> _categoriesUnavailable = [];

    public CompanyResearch(
        Guid id,
        Guid companyId,
        Guid runId,
        string summary,
        IReadOnlyList<ResearchSource> sources,
        IReadOnlyList<ResearchClaim> claims,
        IReadOnlyList<ResearchCategory> categoriesUnavailable,
        int claimsDiscarded,
        string promptVersion,
        DateTimeOffset generatedAt)
        : base(id)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A CompanyResearch must reference a Company.", nameof(companyId));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A CompanyResearch must belong to a Run.", nameof(runId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(categoriesUnavailable);

        if (claimsDiscarded < 0)
        {
            throw new ArgumentException("The discarded-claim count cannot be negative.", nameof(claimsDiscarded));
        }

        var sourceIds = sources.Select(s => s.Id).ToHashSet();
        foreach (var claim in claims)
        {
            // The whole design: a claim may only cite a source this dossier actually fetched and stored.
            // A claim resting on an unrecorded source would be an uncited claim by the back door.
            if (!sourceIds.Contains(claim.SourceId))
            {
                throw new ArgumentException(
                    "A claim cites a source that is not among this dossier's recorded sources (invariant 5).",
                    nameof(claims));
            }
        }

        var covered = claims.Select(c => c.Category).ToHashSet();
        var overlap = covered.Intersect(categoriesUnavailable).ToList();
        if (overlap.Count > 0)
        {
            // A category that produced a claim is covered; declaring it unavailable too would let the
            // dossier say "no blog found" while showing a blog claim.
            throw new ArgumentException(
                "A category cannot be both covered by a claim and recorded as unavailable (AC-07).",
                nameof(categoriesUnavailable));
        }

        CompanyId = companyId;
        RunId = runId;
        Summary = summary.Trim();
        ClaimsDiscarded = claimsDiscarded;
        PromptVersion = promptVersion.Trim();
        GeneratedAt = generatedAt;

        _sources = [.. sources];
        // Warnings first (AC-04), then a stable order by category so rendering is deterministic.
        _claims = [.. claims.OrderByDescending(c => c.IsWarning).ThenBy(c => c.Category)];
        _categoriesUnavailable = [.. categoriesUnavailable.Distinct()];
    }

    private CompanyResearch()
    {
    }

    public Guid CompanyId { get; private set; }

    public Guid RunId { get; private set; }

    /// <summary>Two or three sentences, itself constrained to the cited claims.</summary>
    public string Summary { get; private set; } = null!;

    /// <summary>Uncited claims dropped during verification — a rising value warns the prompt is drifting.</summary>
    public int ClaimsDiscarded { get; private set; }

    public string PromptVersion { get; private set; } = null!;

    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Every document fetched, stored before synthesis — the citation authority.</summary>
    public IReadOnlyList<ResearchSource> Sources => new ReadOnlyCollection<ResearchSource>(_sources);

    /// <summary>The cited claims, warnings first (AC-04). Every claim rests on a recorded source.</summary>
    public IReadOnlyList<ResearchClaim> Claims => new ReadOnlyCollection<ResearchClaim>(_claims);

    /// <summary>The categories a claim speaks to — derived from the claims, never stored independently.</summary>
    public IReadOnlyList<ResearchCategory> CategoriesCovered =>
        _claims.Select(c => c.Category).Distinct().OrderBy(c => c).ToList();

    /// <summary>The categories that produced nothing — recorded explicitly (AC-07).</summary>
    public IReadOnlyList<ResearchCategory> CategoriesUnavailable =>
        new ReadOnlyCollection<ResearchCategory>(_categoriesUnavailable);
}
