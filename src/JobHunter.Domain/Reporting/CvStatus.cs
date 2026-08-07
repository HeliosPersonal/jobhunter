namespace JobHunter.Domain.Reporting;

/// <summary>
/// The <strong>metadata-only</strong> status of the active CV (F10 <c>/cv</c>, catalogue §Profile). It carries
/// the active version number, when that version was activated, and how many current matches were computed
/// against it — and <strong>nothing of the CV's content</strong>. The CV crosses exactly one boundary (the F4
/// match prompt), and it is not this one: <c>extracted_text</c> is never read into this DTO, which is why the
/// F4 leakage scan can leave the <c>/cv</c> path uncovered by construction rather than by an allowlist.
/// </summary>
/// <param name="Version">The active CV version number the Owner uploaded (1-based).</param>
/// <param name="ActivatedAt">When this version became active, or null if it has not been activated.</param>
/// <param name="MatchCount">How many current matches were computed against this version.</param>
public sealed record CvStatus(short Version, DateTimeOffset? ActivatedAt, int MatchCount);
