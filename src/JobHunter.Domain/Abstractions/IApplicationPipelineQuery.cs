using JobHunter.Domain.Applications;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over the pipeline view (F6 [[contracts/application-api]] §Pipeline response, AC-01): the
/// non-archived applications grouped by status, each group most-recently-active first. Read-only (Dapper,
/// architecture rule 4); defined in Domain so the command and API handlers depend on the port, not the SQL.
///
/// <para><paramref name="now"/> is passed in (never <c>DateTime.Now</c>, coding-standards §IClock) so
/// <see cref="PipelineEntry.DaysInStage"/> is computed against the caller's clock and the read is
/// deterministic under test.</para>
/// </summary>
public interface IApplicationPipelineQuery
{
    /// <summary>The pipeline as of <paramref name="now"/>: non-archived applications grouped by status.</summary>
    Task<ApplicationPipeline> PipelineAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
