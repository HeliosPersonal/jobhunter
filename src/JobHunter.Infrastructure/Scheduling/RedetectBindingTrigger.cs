using JobHunter.Application.Discovery;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for daily binding re-detection (SAD §6.2, T09). Hangfire invokes
/// <see cref="PublishAsync"/> on the cron; it does nothing but publish one <see cref="RedetectBindingsDue"/>
/// onto the durable bus, stamping the window from <see cref="IClock"/>. Kept this thin so the run's own
/// logic (which companies are due today, the migration) lives in an Application handler unit-tested without
/// Hangfire. The daily cadence with per-company day buckets is what spreads the weekly re-probe (AC-05).
/// </summary>
internal sealed class RedetectBindingTrigger(IMessageBus bus, IClock clock, ILogger<RedetectBindingTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<RedetectBindingTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var windowStart = _clock.UtcNow;
        _logger.LogInformation("Publishing binding re-detection tick for window {WindowStart:o}.", windowStart);
        await _bus.PublishAsync(new RedetectBindingsDue(windowStart)).ConfigureAwait(false);
    }
}
