using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Preferences;

/// <summary>
/// Switches a specific learned weight off at the Owner's request (T08, AC-06) — the shared write path behind
/// the API disable endpoint and the Telegram override command. It loads the one active
/// <see cref="PreferenceModel"/>, finds the addressed weight among its (owned) weights, calls
/// <see cref="PreferenceWeight.Disable"/>, and commits. The exclusion already in
/// <see cref="PreferenceComponentCalculator"/> then keeps the weight out of the very next ranking, so "immediate"
/// is a property of the read path and this handler only has to record the switch-off.
///
/// <para>The weight is never deleted — a disabled preference stays inspectable, which is the whole of the
/// explainability contract (ADR-F7-0002). <see cref="PreferenceWeight.Disable"/> is idempotent and keeps the
/// first timestamp, so a redelivered request is safe and the explanation's referenced instant is stable. An
/// unknown id (or no active model at all) is a value-typed <see cref="DisablePreferenceWeightResult.WeightNotFound"/>,
/// not an exception (coding-standards §4), and nothing is committed.</para>
/// </summary>
public sealed class DisablePreferenceWeightHandler(
    IPreferenceModelRepository models,
    ILogger<DisablePreferenceWeightHandler> logger)
{
    private readonly IPreferenceModelRepository _models = models ?? throw new ArgumentNullException(nameof(models));
    private readonly ILogger<DisablePreferenceWeightHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<DisablePreferenceWeightOutcome> Handle(
        DisablePreferenceWeightCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var model = await _models.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        var weight = model?.Weights.FirstOrDefault(w => w.Id == command.WeightId);
        if (weight is null)
        {
            // No active model, or no weight with that id: a status the caller renders, not a fault.
            _logger.LogInformation("Disable refused: no active weight {WeightId}.", command.WeightId);
            return DisablePreferenceWeightOutcome.NotFound();
        }

        weight.Disable(command.OccurredAt);
        await _models.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Owner disabled preference weight {WeightId} ({Dimension}={Value}) at {OccurredAt:o}.",
            weight.Id, weight.Dimension, weight.Value, command.OccurredAt);

        return DisablePreferenceWeightOutcome.Disabled(WeightExplanation.Describe(weight));
    }
}
