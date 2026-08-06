using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Preferences;

/// <summary>
/// The weekly refit (F7 SAD §6.1, T05): on a <see cref="RecomputePreferencesDue"/> tick it loads the
/// 180-day signal window, runs the pure <see cref="WeightFitter"/> over it, inserts a new
/// <see cref="PreferenceModel"/> version, and — only with enough evidence — deactivates the prior active
/// model and activates the new one in one <see cref="IPreferenceModelRepository.SaveChangesAsync"/>, so
/// <c>uq_preference_models_active</c> is never momentarily violated and a bad refit is a rollback rather than
/// an incident (S6). Discovered and constructed by Wolverine like every other pipeline handler.
///
/// <para>The evidence floor is <see cref="PreferenceModel.ActivationThreshold"/> (200 signals, ADR-F7-0002):
/// below it no model is activated, but a new inactive version is still written with the reason on its
/// <c>notes</c> ("insufficient evidence: 143 signals"), so the absence of learning is visible rather than
/// mysterious and the previously active model is left exactly as it was (AC-02). Only when a model is
/// activated does the handler publish <see cref="PreferenceModelUpdated"/> for F4 ranking (AC-01). Everything
/// is committed in a single save so the deactivate/activate flip and the outbox publish are atomic.</para>
///
/// <para>The fit is a pure function of public job facts snapshotted on each signal — the CV crosses exactly
/// one boundary, and it is not this one. <see cref="RecomputePreferencesDue.FittedAt"/> is the recency "now"
/// and the instant the new version is fitted and activated at, so a redelivered tick reads the same window
/// and fits the same model.</para>
/// </summary>
public sealed class PreferenceLearner(
    ISignalWindowQuery signals,
    IPreferenceModelRepository models,
    IIdGenerator ids,
    ILogger<PreferenceLearner> logger)
{
    private readonly ISignalWindowQuery _signals = signals ?? throw new ArgumentNullException(nameof(signals));
    private readonly IPreferenceModelRepository _models = models ?? throw new ArgumentNullException(nameof(models));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly ILogger<PreferenceLearner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(RecomputePreferencesDue message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var options = new FittingOptions(message.FittedAt);
        var cutoff = message.FittedAt - options.Window;

        var window = await _signals.LoadSince(cutoff, cancellationToken).ConfigureAwait(false);
        var fitted = WeightFitter.Fit(window, options);

        var latest = await _models.LatestVersionAsync(cancellationToken).ConfigureAwait(false);
        var version = (latest ?? 0) + 1;
        var modelId = _ids.NewId();

        var sufficient = fitted.SignalCount >= PreferenceModel.ActivationThreshold;
        if (!sufficient)
        {
            // Below the floor a rate is coincidence, not preference. Record the reason on a new inactive
            // version and leave the previously active model untouched — a rollback, not an incident (AC-02).
            var notes = $"insufficient evidence: {fitted.SignalCount} signals";
            _models.Add(new PreferenceModel(modelId, version, fitted.SignalCount, [], message.FittedAt, notes));
            await _models.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Refit at {FittedAt:o} produced version {Version} but did not activate it: {Notes}.",
                message.FittedAt, version, notes);
            return;
        }

        var weights = fitted.Weights
            .Select(w => new PreferenceWeight(
                _ids.NewId(), modelId, w.Dimension, w.Value, w.Weight, w.SupportingSignalIds,
                w.PositiveRate, message.FittedAt))
            .ToList();

        var model = new PreferenceModel(modelId, version, fitted.SignalCount, weights, message.FittedAt);

        // Deactivate the prior active model and activate the new one in the same save (S6): the flip is atomic,
        // so a concurrent ranking sees exactly one active model — old or new, never zero and never two.
        var prior = await _models.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        prior?.Deactivate();
        model.Activate(message.FittedAt);
        _models.Add(model);

        // Publish inside the same transaction as the flip (the outbox commits with SaveChangesAsync), so a
        // consumer reacting to PreferenceModelUpdated always finds the newly active model (AC-01).
        await bus.PublishAsync(new PreferenceModelUpdated(
            model.Id, model.Version, model.SignalCount, message.FittedAt, message.FittedAt)).ConfigureAwait(false);
        await _models.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Refit at {FittedAt:o} activated preference model version {Version} on {SignalCount} signals with "
            + "{WeightCount} weight(s).",
            message.FittedAt, version, fitted.SignalCount, weights.Count);
    }
}
