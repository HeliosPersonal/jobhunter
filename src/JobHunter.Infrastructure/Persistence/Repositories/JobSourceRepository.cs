using JobHunter.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for operational sources and their fetch logs (data-model §job_sources,
/// §source_fetch_log). Health-state transitions (success/failure/quarantine) mutate the tracked
/// <see cref="JobSource"/> and are persisted through <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IJobSourceRepository
{
    Task AddAsync(JobSource source, CancellationToken cancellationToken = default);

    Task<JobSource?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<JobSource?> FindByBindingAsync(Guid bindingId, CancellationToken cancellationToken = default);

    Task AddFetchLogAsync(SourceFetchLog log, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class JobSourceRepository(JobHunterDbContext context) : IJobSourceRepository
{
    public async Task AddAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await context.Set<JobSource>().AddAsync(source, cancellationToken);
    }

    public Task<JobSource?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Set<JobSource>().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<JobSource?> FindByBindingAsync(Guid bindingId, CancellationToken cancellationToken = default) =>
        context.Set<JobSource>().FirstOrDefaultAsync(s => s.BindingId == bindingId, cancellationToken);

    public async Task AddFetchLogAsync(SourceFetchLog log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        await context.Set<SourceFetchLog>().AddAsync(log, cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
