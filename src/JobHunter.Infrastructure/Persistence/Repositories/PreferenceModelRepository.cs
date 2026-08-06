using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write repository for the <see cref="PreferenceModel"/> aggregate (F7 data-model §preference_models).
/// Models go through EF as owned aggregates — the weights are written with the model in one insert. Activation
/// is the caller's atomic responsibility (SAD §4 S6): a refit deactivates the prior active model and activates
/// the new one in the same <see cref="SaveChangesAsync"/>, so <c>uq_preference_models_active</c> is never
/// momentarily violated. There is no update path for a model's weights — a model is immutable once written.
/// </summary>
public sealed class PreferenceModelRepository(JobHunterDbContext context) : IPreferenceModelRepository
{
    public void Add(PreferenceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        context.Set<PreferenceModel>().Add(model);
    }

    public Task<PreferenceModel?> FindActiveAsync(CancellationToken cancellationToken = default) =>
        context.Set<PreferenceModel>()
            .Include(m => m.Weights)
            .FirstOrDefaultAsync(m => m.IsActive, cancellationToken);

    public async Task<int?> LatestVersionAsync(CancellationToken cancellationToken = default)
    {
        var any = await context.Set<PreferenceModel>().AnyAsync(cancellationToken);
        if (!any)
        {
            return null;
        }

        return await context.Set<PreferenceModel>().MaxAsync(m => m.Version, cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
