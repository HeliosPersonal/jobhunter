using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write repository for the <see cref="App"/> aggregate and the transitions and notes that hang off it
/// (F6 data-model §applications). One application per job is a database constraint (unique <c>job_id</c>),
/// not a check here. The transition history is append-only (QG-1): the port offers a stage, a read and a
/// commit — <b>no update and no delete path</b>, so a correction can only ever be a new transition.
/// </summary>
public interface IApplicationRepository
{
    /// <summary>Stages an application with its transitions and notes for insert in one transaction.</summary>
    void Add(App application);

    /// <summary>
    /// The application for a job with its transitions (occurrence-ordered) and notes, or null. This is what
    /// the owner-action handler loads to advance an existing application rather than create a duplicate.
    /// </summary>
    Task<App?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Commits the staged changes in one transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
