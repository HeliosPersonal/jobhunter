using JobHunter.Domain.Preferences;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write repository for the <see cref="PreferenceModel"/> aggregate and its owned
/// <see cref="PreferenceWeight"/> children (F7 data-model §preference_models/§preference_weights). A weekly
/// refit inserts a new version and flips activation atomically (SAD §4 S6): the previously active model is
/// deactivated and the new one activated inside one transaction, so "exactly one active model"
/// (<c>uq_preference_models_active</c>) holds and a bad refit is a rollback to the prior version rather than
/// an incident. Models are immutable once written; there is no update path for their weights.
/// </summary>
public interface IPreferenceModelRepository
{
    /// <summary>Stages a model and its weights for insert in one transaction.</summary>
    void Add(PreferenceModel model);

    /// <summary>The active model with its weights, or null when none has been activated yet.</summary>
    Task<PreferenceModel?> FindActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>The highest version present, or null when no model has been fitted — the basis for the next version number.</summary>
    Task<int?> LatestVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits the staged changes in one transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
