using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Preferences;

/// <summary>
/// The persisted <see cref="ILearningSwitch"/> (F7 T08 C4, AC-07): the live, runtime-flippable master switch
/// both <c>PreferenceModelQuery</c> and the digest assembler consult. It reads the single
/// <see cref="LearningState"/> row through the tracked context; when that row is absent — the switch has never
/// been flipped — it falls back to the configured <see cref="LearningOptions.Enabled"/> seed, so a fresh
/// install behaves exactly as the boot default until the Owner changes it. Writing upserts the one row at its
/// fixed singleton id and commits, so the next ranking and the next digest see the new state immediately.
/// </summary>
internal sealed class PersistentLearningSwitch(JobHunterDbContext context, LearningOptions seed) : ILearningSwitch
{
    private readonly JobHunterDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly LearningOptions _seed = seed ?? throw new ArgumentNullException(nameof(seed));

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        var state = await _context.Set<LearningState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == LearningState.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        // No row means the switch has never been flipped: use the configured seed default, so a fresh install
        // ranks and reports exactly as the boot config until the Owner changes it.
        return state?.Enabled ?? _seed.Enabled;
    }

    public async Task SetAsync(bool enabled, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        var state = await _context.Set<LearningState>()
            .FirstOrDefaultAsync(x => x.Id == LearningState.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        if (state is null)
        {
            _context.Add(new LearningState(LearningState.SingletonId, enabled, occurredAt));
        }
        else
        {
            state.Set(enabled, occurredAt);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
