using JobHunter.Domain.Sources;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write port for operational sources and their fetch logs (data-model §job_sources,
/// §source_fetch_log). Health-state transitions (success/failure/quarantine) mutate the tracked
/// <see cref="JobSource"/> and are persisted through <see cref="SaveChangesAsync"/>. Defined in Domain
/// so the discovery handlers depend on the port, not the EF Core implementation.
/// </summary>
public interface IJobSourceRepository
{
    Task AddAsync(JobSource source, CancellationToken cancellationToken = default);

    Task<JobSource?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<JobSource?> FindByBindingAsync(Guid bindingId, CancellationToken cancellationToken = default);

    Task AddFetchLogAsync(SourceFetchLog log, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
