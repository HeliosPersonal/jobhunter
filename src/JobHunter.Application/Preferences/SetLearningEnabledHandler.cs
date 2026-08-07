using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Preferences;

/// <summary>
/// Turns preference learning on or off at the Owner's request (T08, done-when 4, AC-07) — the shared write path
/// behind the API learning endpoint and the Telegram override command. It reads the current persisted state
/// from <see cref="ILearningSwitch"/> and only writes when the requested state differs, so a redelivered
/// request is an idempotent no-op rather than a redundant write, and reports whether it actually changed
/// anything.
///
/// <para>The switch is deliberately non-destructive: turning learning off deletes no signal. The next ranking
/// simply finds the switch off and renormalises the learned preference weight away, ordering on match,
/// freshness and explicit Profile preferences alone, and the next digest states that learning is off — the
/// evidence survives intact for when it is turned back on.</para>
/// </summary>
public sealed class SetLearningEnabledHandler(
    ILearningSwitch learning,
    ILogger<SetLearningEnabledHandler> logger)
{
    private readonly ILearningSwitch _learning = learning ?? throw new ArgumentNullException(nameof(learning));
    private readonly ILogger<SetLearningEnabledHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<SetLearningEnabledOutcome> Handle(
        SetLearningEnabledCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var current = await _learning.IsEnabledAsync(cancellationToken).ConfigureAwait(false);
        if (current == command.Enabled)
        {
            // Already in the requested state: an idempotent no-op, not a redundant write.
            _logger.LogInformation("Learning already {State}; no change.", command.Enabled ? "on" : "off");
            return new SetLearningEnabledOutcome(command.Enabled, Changed: false);
        }

        await _learning.SetAsync(command.Enabled, command.OccurredAt, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Owner turned preference learning {State} at {OccurredAt:o}; no signal deleted.",
            command.Enabled ? "on" : "off", command.OccurredAt);

        return new SetLearningEnabledOutcome(command.Enabled, Changed: true);
    }
}
