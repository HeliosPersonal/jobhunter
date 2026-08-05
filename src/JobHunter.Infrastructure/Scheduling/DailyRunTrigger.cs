using JobHunter.Application.Enrichment;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the 02:00 Europe/Kyiv daily run start (F5 SAD §6.3, ADR-F5-0001). Hangfire
/// invokes <see cref="PublishAsync"/> on the cron; it does nothing but publish one
/// <see cref="StartDailyRun"/> onto the durable bus, stamping the window end from <see cref="IClock"/>.
/// Opening the Run five hours before the 07:00 delivery slot is what guarantees a Run row exists by the
/// 06:45 assembly tick, so even a degraded day still has a Run to assemble a digest against rather than
/// silence — the true "no Run at all" case is the 02:00 tick never firing, which R1 alerts on. The
/// message is idempotent at the orchestrator (a live Run makes it a no-op), so a redelivered tick never
/// starts a second Run.
/// </summary>
internal sealed class DailyRunTrigger(IMessageBus bus, IClock clock, ILogger<DailyRunTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<DailyRunTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var windowEnd = _clock.UtcNow;
        _logger.LogInformation("Publishing daily run start for window end {WindowEnd:o}.", windowEnd);
        await _bus.PublishAsync(new StartDailyRun(windowEnd)).ConfigureAwait(false);
    }
}
