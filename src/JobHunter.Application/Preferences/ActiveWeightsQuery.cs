using JobHunter.Domain.Abstractions;

namespace JobHunter.Application.Preferences;

/// <summary>
/// Lists the active model's learned weights as the Owner sees them before switching any off (T08 C6,
/// AC-03/AC-06): the shared read behind the API weights endpoint and the Telegram override command. It loads
/// the one active <see cref="Domain.Preferences.PreferenceModel"/> through the write repository — there is no
/// SQL of its own — and projects each owned weight into a <see cref="LearnedWeight"/>, pairing its id with the
/// one plain sentence <see cref="WeightExplanation"/> renders, so both surfaces quote identical text.
///
/// <para>Ordered by the strength of the pull (largest absolute weight first) so the preferences shaping the
/// ranking most are the ones the Owner reads first, and a disabled weight is still listed and flagged — it
/// stays inspectable, which is the whole of the explainability contract (ADR-F7-0002). No active model is an
/// empty list, never a fault (coding-standards §4). Deliberately in Application, composing an existing
/// repository and the pure explainer; registered scoped because it holds the scoped repository.</para>
/// </summary>
public sealed class ActiveWeightsQuery(IPreferenceModelRepository models)
{
    private readonly IPreferenceModelRepository _models = models ?? throw new ArgumentNullException(nameof(models));

    /// <summary>The active model's weights, strongest pull first, each with its id and one-sentence explanation.</summary>
    public async Task<IReadOnlyList<LearnedWeight>> ActiveAsync(CancellationToken cancellationToken = default)
    {
        var model = await _models.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return [];
        }

        return model.Weights
            .OrderByDescending(w => Math.Abs(w.Weight))
            .ThenBy(w => w.Value, StringComparer.Ordinal)
            .Select(w => new LearnedWeight(
                w.Id, w.Dimension, w.Value, w.Weight, w.Disabled, WeightExplanation.Describe(w)))
            .ToList();
    }
}
