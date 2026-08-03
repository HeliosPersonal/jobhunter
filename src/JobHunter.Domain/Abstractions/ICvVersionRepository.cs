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

    /// <summary>
    /// The next monotonic version number for a profile: one past the highest existing version, or 1 when
    /// the profile has none yet. The version sequence never reuses a number, so history stays ordered.
    /// </summary>
    Task<short> NextVersionAsync(Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a freshly-built version: in one transaction it deactivates the profile's currently-active
    /// version (if any) and inserts the new one, so there is never a moment with two active versions for a
    /// profile that <c>uq_cv_versions_active</c> would reject (ADR-F4-0002). The deactivating UPDATE is
    /// flushed before the INSERT, and the whole thing commits or rolls back together.
    /// </summary>
    Task ActivateAsync(CvVersion newVersion, CancellationToken cancellationToken = default);

    /// <summary>Finds a CV version by id, or null.</summary>
    Task<CvVersion?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds an existing version for a profile with the given content hash, or null.</summary>
    Task<CvVersion?> FindByHashAsync(Guid profileId, string contentHash, CancellationToken cancellationToken = default);

    /// <summary>Finds the active CV version for a profile, or null when none is active.</summary>
    Task<CvVersion?> FindActiveAsync(Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>Commits staged changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
