using JobHunter.Domain.Profiles;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write repository for the <see cref="CvVersion"/> aggregate (data-model §cv_versions,
/// ADR-F4-0002). A version is immutable — a new upload is a new row — so there is no update path. The
/// partial unique index <c>uq_cv_versions_active</c> enforces one active version per profile, and
/// <c>uq_cv_versions_hash</c> makes re-uploading identical content a recognised no-op rather than a new
/// version.
/// </summary>
public interface ICvVersionRepository
{
    /// <summary>Stages a new CV version for insertion; the unit of work commits it.</summary>
    void Add(CvVersion cvVersion);

    /// <summary>Finds a CV version by id, or null.</summary>
    Task<CvVersion?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds an existing version for a profile with the given content hash, or null.</summary>
    Task<CvVersion?> FindByHashAsync(Guid profileId, string contentHash, CancellationToken cancellationToken = default);

    /// <summary>Finds the active CV version for a profile, or null when none is active.</summary>
    Task<CvVersion?> FindActiveAsync(Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>Commits staged changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
