using JobHunter.Application.Preferences;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the weekly preference refit (F7 SAD §6.1, T05). Hangfire invokes
/// <see cref="PublishAsync"/> on the Monday 03:00 Europe/Kyiv cron — a quiet hour, weekly rather than
/// continuous so one bad day cannot move the model (SAD §4 S3). It does nothing but publish one
/// <see cref="RecomputePreferencesDue"/> onto the durable bus, stamping the refit instant from
/// <see cref="IClock"/>. Kept this thin so the refit's own logic (load the window, fit, version, activate)
/// lives in the <see cref="PreferenceLearner"/> Application handler, unit-tested without Hangfire.
/// </summary>
internal sealed class PreferenceRefitTrigger(IMessageBus bus, IClock clock, ILogger<PreferenceRefitTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<PreferenceRefitTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var fittedAt = _clock.UtcNow;
        _logger.LogInformation("Publishing weekly preference refit tick for instant {FittedAt:o}.", fittedAt);
        await _bus.PublishAsync(new RecomputePreferencesDue(fittedAt)).ConfigureAwait(false);
    }
}
