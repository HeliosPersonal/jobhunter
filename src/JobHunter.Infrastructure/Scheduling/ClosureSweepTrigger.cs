using JobHunter.Application.Discovery;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the closure sweep (SAD §6.1, T13). Hangfire invokes <see cref="PublishAsync"/>
/// on the cron; it does nothing but publish one <see cref="ClosureSweepDue"/> onto the durable bus, stamping
/// the window from <see cref="IClock"/>. Kept this thin so the sweep's own logic (which postings are gone,
/// the fan-out of <c>JobClosed</c>) lives in an Application handler unit-tested without Hangfire.
/// </summary>
internal sealed class ClosureSweepTrigger(IMessageBus bus, IClock clock, ILogger<ClosureSweepTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<ClosureSweepTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var windowStart = _clock.UtcNow;
        _logger.LogInformation("Publishing closure sweep tick for window {WindowStart:o}.", windowStart);
        await _bus.PublishAsync(new ClosureSweepDue(windowStart)).ConfigureAwait(false);
    }
}
