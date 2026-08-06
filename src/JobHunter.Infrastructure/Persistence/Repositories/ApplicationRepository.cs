using JobHunter.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the <see cref="App"/> aggregate, its transitions and its notes (F6 data-model
/// §applications). One application per job is a database constraint (<c>uq_applications_job</c>), so a
/// second application for the same job fails at commit rather than duplicating. The history is append-only
/// (QG-1): this type offers a stage, a read and a commit — no update and no delete path — so a correction
/// can only be a new transition appended through the aggregate.
/// </summary>
public sealed class ApplicationRepository(JobHunterDbContext context) : IApplicationRepository
{
    public void Add(App application)
    {
        ArgumentNullException.ThrowIfNull(application);
        context.Set<App>().Add(application);
    }

    public Task<App?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        context.Set<App>()
            .Include(a => a.Transitions.OrderBy(t => t.OccurredAt))
            .Include(a => a.Notes.OrderBy(n => n.CreatedAt))
            .FirstOrDefaultAsync(a => a.JobId == jobId, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
