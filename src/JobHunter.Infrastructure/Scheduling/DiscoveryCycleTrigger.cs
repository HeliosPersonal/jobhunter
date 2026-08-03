using JobHunter.Application.Discovery;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the six-hourly discovery cycle (SAD §6.1). Hangfire invokes
/// <see cref="PublishAsync"/> on the cron; it does nothing but publish one <see cref="DiscoveryCycleDue"/>
/// onto the durable bus, stamping the window from <see cref="IClock"/>. Keeping the schedule trigger this
/// thin means the cycle's own logic (which sources are due, the fan-out) lives in an Application handler
/// that is unit-tested without Hangfire, and the recurring job is just "tick the bus every six hours".
/// </summary>
internal sealed class DiscoveryCycleTrigger(IMessageBus bus, IClock clock, ILogger<DiscoveryCycleTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<DiscoveryCycleTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var windowStart = _clock.UtcNow;
        _logger.LogInformation("Publishing discovery cycle tick for window {WindowStart:o}.", windowStart);
        await _bus.PublishAsync(new DiscoveryCycleDue(windowStart)).ConfigureAwait(false);
    }
}
