namespace JobHunter.Domain.Reporting;

/// <summary>
/// One role the Owner saved from a digest card (F5 T11 <c>/saved</c>), reduced to what the command renders
/// as a card. A save is a <c>Saved</c>-kind signal (F7 owns the <c>signals</c> table); this read model joins
/// that signal back to the job, its company, its latest score and its current match so <c>/saved</c> shows
/// the same scannable card the morning digest did — title, company · stage · location, salary · score, and
/// the ranking's own reasons (AC-12, invariant 4).
///
/// <para>It carries the raw display primitives, not a composed layout: the Telegram layer maps this to a
/// <c>CardView</c> and renders it through the one shared <c>CardFormatter</c>, so there is no second layout.
/// It carries <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is not
/// this one (F4 invariant).</para>
/// </summary>
/// <param name="JobId">The saved job — the card's subject.</param>
/// <param name="Title">The job title (raw board text; escaped and truncated by the formatter).</param>
/// <param name="Company">The company display name.</param>
/// <param name="Stage">The company funding stage, or null/blank when unknown (then omitted).</param>
/// <param name="Countries">The distinct countries of the job's locations; empty when only a remote policy.</param>
/// <param name="RemotePolicy">The remote policy, shown as the location when no country is known.</param>
/// <param name="SalaryMin">The published salary lower bound in whole currency units, or null.</param>
/// <param name="SalaryMax">The published salary upper bound in whole currency units, or null.</param>
/// <param name="SalaryCurrency">The salary currency code, or null/blank to omit.</param>
/// <param name="Score">The role's final 0–100 score, as shown when it was delivered.</param>
/// <param name="Reasons">The current match's reasons — the ranking's own explanation (invariant 4).</param>
/// <param name="SavedAt">When the Owner saved it, so <c>/saved</c> can order newest-first.</param>
public sealed record SavedRole(
    Guid JobId,
    string Title,
    string Company,
    string? Stage,
    IReadOnlyList<string> Countries,
    string RemotePolicy,
    int? SalaryMin,
    int? SalaryMax,
    string? SalaryCurrency,
    decimal Score,
    IReadOnlyList<string> Reasons,
    DateTimeOffset SavedAt);
