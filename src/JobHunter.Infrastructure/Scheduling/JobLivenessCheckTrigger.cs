using JobHunter.Application.Lifecycle;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the daily job-liveness check (SAD §6.2, T08). Hangfire invokes
/// <see cref="PublishAsync"/> on the cron; it does nothing but publish one <see cref="JobLivenessCheckDue"/>
/// onto the durable bus, stamping the staleness cutoff at two discovery cycles before the clock's instant —
/// a job absent from every board for that whole window is gone. Kept this thin so the check's own logic
/// (which jobs are stale, the fan-out of <c>JobClosed</c>) lives in an Application handler unit-tested
/// without Hangfire.
/// </summary>
internal sealed class JobLivenessCheckTrigger(
    IMessageBus bus,
    IClock clock,
    ILogger<JobLivenessCheckTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<JobLivenessCheckTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Two six-hourly discovery cycles: a job unseen this long is stale across all its sources.</summary>
    private static readonly TimeSpan TwoCycles = TimeSpan.FromHours(12);

    public async Task PublishAsync()
    {
        var seenBefore = _clock.UtcNow - TwoCycles;
        _logger.LogInformation("Publishing job-liveness check tick for cutoff {SeenBefore:o}.", seenBefore);
        await _bus.PublishAsync(new JobLivenessCheckDue(seenBefore)).ConfigureAwait(false);
    }
}
