using JobHunter.Domain.Profiles;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write repository for the <see cref="Profile"/> aggregate (data-model §profiles). Exactly one
/// Profile is active at a time; the partial unique index <c>uq_profiles_active</c> is the arbiter, so a
/// second active Profile fails at commit rather than being silently allowed. Single Owner: there is no
/// per-tenant scoping (invariant 9).
/// </summary>
public interface IProfileRepository
{
    /// <summary>Stages a new Profile for insertion; the unit of work commits it.</summary>
    void Add(Profile profile);

    /// <summary>Finds a Profile by id, or null.</summary>
    Task<Profile?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds the one active Profile, or null when none is active.</summary>
    Task<Profile?> FindActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits staged changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
