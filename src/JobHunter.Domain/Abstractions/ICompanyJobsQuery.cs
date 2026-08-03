using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over a single company's live jobs (data-model §jobs), backing the company-detail
/// endpoint's "live jobs" section (F9 T06, API contract §Companies). Read-only (Dapper): it returns only
/// <c>Live</c> jobs for the company, most recent first, and never a closed or quarantined one — so the
/// company page shows what is currently open, not history. Defined in Domain so the API depends on the
/// port, not the Infrastructure query.
/// </summary>
public interface ICompanyJobsQuery
{
    /// <summary>The live jobs belonging to <paramref name="companyId"/>, most recent first.</summary>
    Task<IReadOnlyList<LiveJob>> LiveForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);
}
