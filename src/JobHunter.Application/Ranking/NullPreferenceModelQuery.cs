using JobHunter.Domain.Abstractions;

namespace JobHunter.Application.Ranking;

/// <summary>
/// The default <see cref="IPreferenceModelQuery"/> for before F7 lands (F4 SAD §6.2; the preference model is
/// fitted by F7, which F4 only consumes — epic §Scope). It always answers "no active model", so ranking
/// renormalises the preference weight away and scores on match and freshness alone. When F7 ships its own
/// implementation this registration is simply replaced — the ranking handler does not change.
///
/// <para>Deliberately not an Infrastructure query: there is no preference table to read yet, so a real Dapper
/// query would only ever return null. Keeping the null here means the pipeline runs end-to-end today without a
/// schema that does not exist, and F7's arrival is an additive swap rather than a rewrite.</para>
/// </summary>
public sealed class NullPreferenceModelQuery : IPreferenceModelQuery
{
    public Task<ActivePreference?> FindActiveAsync(
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);
        return Task.FromResult<ActivePreference?>(null);
    }
}
