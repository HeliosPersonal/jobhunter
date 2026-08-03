using System.Collections.Frozen;

namespace JobHunter.Domain.Search;

/// <summary>
/// The one document that ever reaches the search index — a <strong>hand-written allowlist</strong> of
/// exactly the fields that may be searched, and deliberately <em>not</em> a mapping from the
/// <see cref="Jobs.Job"/> aggregate (SAD §4 S3, ADR-F9-0001 rule 3). Adding a field to <c>Job</c> —
/// including any that might one day carry CV-derived text — cannot reach the index without someone
/// editing this record, which is the structural half of QG-2 ("nothing private is exposed"). The other
/// half is the index-scan suite, which asserts no CV sentinel and that the indexed field set exactly
/// equals <see cref="FieldNames"/>.
///
/// <para>Nothing that references the CV is present: no match reasons, no missing skills, no interview
/// probability, no application notes, no preference weights (data-model §What is deliberately absent).
/// The fields sourced from features not yet merged — <see cref="AiUsage"/> (F3), <see cref="Score"/>
/// (F4) and <see cref="ApplicationStatus"/> (F6) — are modelled now and populated as their absent value
/// (null, or 0 for an un-ranked score) until those features land.</para>
/// </summary>
public sealed record JobDocument(
    string Id,
    string Title,
    string CompanyName,
    string CompanyDomain,
    string Description,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Countries,
    string RemotePolicy,
    string? Seniority,
    string EmploymentType,
    string? CompanyStage,
    string? AiUsage,
    int? SalaryMin,
    int? SalaryMax,
    string? SalaryCurrency,
    double Score,
    long? PostedAt,
    long FirstSeenAt,
    string Status,
    string? ApplicationStatus)
{
    /// <summary>
    /// The canonical, ordered set of indexed field names — the single source of truth the Typesense
    /// schema is built from (T02) and the index-scan suite asserts against (T10). It is hand-maintained
    /// alongside the record's properties; a test asserts the two never diverge, so a new property that
    /// forgets to appear here (or a name here with no property) is a build failure. camelCase, because
    /// that is what the collection uses.
    /// </summary>
    public static readonly IReadOnlyList<string> FieldNames =
    [
        "id",
        "title",
        "companyName",
        "companyDomain",
        "description",
        "technologies",
        "countries",
        "remotePolicy",
        "seniority",
        "employmentType",
        "companyStage",
        "aiUsage",
        "salaryMin",
        "salaryMax",
        "salaryCurrency",
        "score",
        "postedAt",
        "firstSeenAt",
        "status",
        "applicationStatus",
    ];

    /// <summary>The field names as a set, for fast membership and set-equality assertions.</summary>
    public static readonly FrozenSet<string> FieldNameSet = FieldNames.ToFrozenSet(StringComparer.Ordinal);
}
