using JobHunter.Domain.Applications;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over a single application with its complete transition history and notes (F6
/// [[contracts/application-api]] <c>GET /api/applications/{id}</c>, AC-03). Retrievable by id even when the
/// application has archived out of the pipeline view, so the full record is never lost (SAD §8 Archival).
/// Read-only (Dapper, architecture rule 4); defined in Domain so the handlers depend on the port, not the SQL.
/// </summary>
public interface IApplicationHistoryQuery
{
    /// <summary>The application and its ordered history, or <c>null</c> if no application has that id.</summary>
    Task<ApplicationHistory?> HistoryAsync(Guid applicationId, CancellationToken cancellationToken = default);
}
