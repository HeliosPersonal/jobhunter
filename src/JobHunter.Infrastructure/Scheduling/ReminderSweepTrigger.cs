using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the reminder sweep (F6 SAD §6.2, T06). Hangfire invokes <see cref="PublishAsync"/>
/// on the 08:00 Europe/Kyiv cron — an hour after the 07:00 digest, deliberately separate so the morning
/// message stays about opportunities (done-when 6). It does nothing but publish one <see cref="ReminderSweepDue"/>
/// onto the durable bus, stamping the sweep instant from <see cref="IClock"/>. Kept this thin so the sweep's own
/// logic (which applications are due, the suppression, the reschedule) lives in an Application handler
/// unit-tested without Hangfire.
/// </summary>
internal sealed class ReminderSweepTrigger(IMessageBus bus, IClock clock, ILogger<ReminderSweepTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<ReminderSweepTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var sweptAt = _clock.UtcNow;
        _logger.LogInformation("Publishing reminder sweep tick for instant {SweptAt:o}.", sweptAt);
        await _bus.PublishAsync(new ReminderSweepDue(sweptAt)).ConfigureAwait(false);
    }
}
