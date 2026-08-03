namespace JobHunter.Domain.Companies;

/// <summary>
/// The applicant-tracking systems F1 can fetch from. Persisted as <c>text</c> (never an ordinal —
/// coding-standards §5). <see cref="CareersPage"/> is the Tier-2 JSON-LD fallback and is always the
/// lowest-confidence binding (contract §Career pages).
/// </summary>
public enum AtsKind
{
    /// <summary>Greenhouse job boards — <c>boards-api.greenhouse.io</c>.</summary>
    Greenhouse,

    /// <summary>Lever postings — <c>api.lever.co</c>.</summary>
    Lever,

    /// <summary>Ashby job board — <c>api.ashbyhq.com</c>.</summary>
    Ashby,

    /// <summary>Workable widget accounts — <c>apply.workable.com</c>.</summary>
    Workable,

    /// <summary>A company career page parsed for <c>schema.org/JobPosting</c> JSON-LD (Tier 2).</summary>
    CareersPage,
}
