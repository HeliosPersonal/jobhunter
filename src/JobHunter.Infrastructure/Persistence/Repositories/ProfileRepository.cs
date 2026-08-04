using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The EF Core write repository for the <see cref="Profile"/> aggregate (data-model §profiles). Writes go
/// through the tracked context so the partial unique index <c>uq_profiles_active</c> (one active Profile)
/// is enforced at commit — the repository does not re-check it in code, because the database is the
/// arbiter (SAD S2). Reads go through the same context.
/// </summary>
public sealed class ProfileRepository(JobHunterDbContext context) : IProfileRepository
{
    public void Add(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        context.Add(profile);
    }

    public Task<Profile?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Set<Profile>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Profile?> FindActiveAsync(CancellationToken cancellationToken = default) =>
        context.Set<Profile>().FirstOrDefaultAsync(p => p.IsActive, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
