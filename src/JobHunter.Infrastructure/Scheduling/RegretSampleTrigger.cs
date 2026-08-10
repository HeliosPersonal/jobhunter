using JobHunter.Application.Ratings;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the weekly pre-match-filter regret sample (F4 T21 done-when 1). Hangfire invokes
/// <see cref="PublishAsync"/> on the Monday 09:30 Europe/Kyiv cron — half an hour after the 09:00 rating prompt
/// and well clear of the 07:00 digest, so the diagnostic never crowds the Owner's morning. It does nothing but
/// publish one <see cref="RegretSampleDue"/> onto the durable bus, stamping the due instant from
/// <see cref="IClock"/>. Kept this thin so the sampler's own logic (open the week once, match the excluded
/// sample at the cheap tier, record the gauge, alert on any regret) lives in the <see cref="RegretSampler"/>
/// Application handler, unit-tested without Hangfire.
///
/// <para>Stamping the instant here (not in the handler) is what makes a redelivered tick resolve the same week
/// and open the same sample, so the cheap-tier match is never run — or paid for — twice (done-when 1).</para>
/// </summary>
internal sealed class RegretSampleTrigger(IMessageBus bus, IClock clock, ILogger<RegretSampleTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<RegretSampleTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var dueAt = _clock.UtcNow;
        _logger.LogInformation("Publishing weekly regret-sample tick for instant {DueAt:o}.", dueAt);
        await _bus.PublishAsync(new RegretSampleDue(dueAt)).ConfigureAwait(false);
    }
}
