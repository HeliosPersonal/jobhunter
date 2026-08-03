using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The EF Core write repository for the <see cref="CvVersion"/> aggregate (data-model §cv_versions). A
/// version is immutable, so there is deliberately no update path: a correction is a new version. Writes
/// go through the tracked context so <c>uq_cv_versions_active</c> (one active version per profile) and
/// <c>uq_cv_versions_hash</c> (no duplicate content) are enforced at commit rather than re-checked in
/// code. The hash is normalised to lower-case by the aggregate, so the lookup compares like with like.
/// </summary>
public sealed class CvVersionRepository(JobHunterDbContext context) : ICvVersionRepository
{
    public void Add(CvVersion cvVersion)
    {
        ArgumentNullException.ThrowIfNull(cvVersion);
        context.Add(cvVersion);
    }

    public Task<CvVersion?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Set<CvVersion>().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<CvVersion?> FindByHashAsync(
        Guid profileId,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        var hash = contentHash.Trim().ToLowerInvariant();
        return context.Set<CvVersion>()
            .FirstOrDefaultAsync(v => v.ProfileId == profileId && v.ContentHash == hash, cancellationToken);
    }

    public Task<CvVersion?> FindActiveAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        context.Set<CvVersion>()
            .FirstOrDefaultAsync(v => v.ProfileId == profileId && v.IsActive, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
