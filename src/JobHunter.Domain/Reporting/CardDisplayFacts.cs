namespace JobHunter.Domain.Reporting;

/// <summary>
/// The <em>display</em> facts a digest card shows for one job (F5 T12), joined at render time rather than
/// snapshotted on the card. A <see cref="DigestCard"/> carries the score and reasons it was assembled with
/// (invariant 4), but not the title, company, location or salary — those are the job's own and are read
/// fresh so a re-rendered <c>/digest</c> shows the job as it stands. This record carries the raw display
/// primitives; the Telegram layer maps them onto the one shared <c>CardView</c>/<c>CardFormatter</c>, so
/// there is no second layout.
///
/// <para>Salary is carried in two forms: the job's <em>published</em> range when the board stated one, and
/// the model's most recent <em>estimate</em> (with its confidence) as the fallback the card marks
/// <c>(est)</c> — never presented as fact. It carries <strong>nothing about the Owner</strong>: the CV
/// crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
/// <param name="JobId">The job the card is about.</param>
/// <param name="Title">The job title (raw board text; escaped and truncated by the formatter).</param>
/// <param name="Company">The company display name.</param>
/// <param name="Stage">The company funding stage, or null/blank when unknown (then omitted).</param>
/// <param name="Countries">The distinct countries of the job's locations; empty when only a remote policy.</param>
/// <param name="RemotePolicy">The remote policy, shown as the location when no country is known.</param>
/// <param name="ApplyUrl">The verified apply link the card's Open button points at.</param>
/// <param name="PublishedSalaryMin">The as-published salary lower bound in whole currency units, or null.</param>
/// <param name="PublishedSalaryMax">The as-published salary upper bound in whole currency units, or null.</param>
/// <param name="PublishedSalaryCurrency">The published salary currency code, or null/blank to omit.</param>
/// <param name="EstimatedSalaryMin">The latest enrichment estimate's lower bound, or null when none.</param>
/// <param name="EstimatedSalaryMax">The latest enrichment estimate's upper bound, or null when none.</param>
/// <param name="EstimatedSalaryCurrency">The estimate's currency code, or null/blank.</param>
/// <param name="EstimatedSalaryConfidence">The estimate's confidence in [0,1], shown as the (est) band.</param>
public sealed record CardDisplayFacts(
    Guid JobId,
    string Title,
    string Company,
    string? Stage,
    IReadOnlyList<string> Countries,
    string RemotePolicy,
    string ApplyUrl,
    int? PublishedSalaryMin,
    int? PublishedSalaryMax,
    string? PublishedSalaryCurrency,
    int? EstimatedSalaryMin,
    int? EstimatedSalaryMax,
    string? EstimatedSalaryCurrency,
    decimal? EstimatedSalaryConfidence);
