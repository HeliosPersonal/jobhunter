using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Preferences;

/// <summary>
/// Resets preference learning at the Owner's request (T08, done-when 3) — the shared write path behind the API
/// reset endpoint and the Telegram override command. It loads the one active <see cref="PreferenceModel"/>,
/// calls <see cref="PreferenceModel.Deactivate"/>, and commits. With no model active it reports
/// <see cref="ResetPreferenceModelResult.NothingActive"/> and commits nothing.
///
/// <para>A full reset is deliberately non-destructive: the model is deactivated, never deleted, and no
/// <c>Signal</c> is touched. The model stays queryable (a rollback is a flag change, S6), and the signals the
/// Owner was reacting to remain the evidence a future refit rebuilds from. Until that refit runs, F4 ranking
/// simply finds no active model and falls back to the explicit-preference floor. <see cref="PreferenceModel.Deactivate"/>
/// is idempotent, so a redelivered request is safe.</para>
/// </summary>
public sealed class ResetPreferenceModelHandler(
    IPreferenceModelRepository models,
    ILogger<ResetPreferenceModelHandler> logger)
{
    private readonly IPreferenceModelRepository _models = models ?? throw new ArgumentNullException(nameof(models));
    private readonly ILogger<ResetPreferenceModelHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<ResetPreferenceModelOutcome> Handle(
        ResetPreferenceModelCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var active = await _models.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        if (active is null)
        {
            // Nothing to reset: a status the caller renders, not a fault.
            _logger.LogInformation("Reset refused: no active preference model.");
            return ResetPreferenceModelOutcome.NothingActive();
        }

        var version = active.Version;
        active.Deactivate();
        await _models.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Owner reset preference learning at {OccurredAt:o}: deactivated model version {Version}; no signal deleted.",
            command.OccurredAt, version);

        return ResetPreferenceModelOutcome.Reset(version);
    }
}
