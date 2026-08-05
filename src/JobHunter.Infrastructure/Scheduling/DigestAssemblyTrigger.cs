using JobHunter.Application.Reporting;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the 06:45 Europe/Kyiv digest assembly deadline (F5 SAD §6.3, ADR-F5-0001).
/// Hangfire invokes <see cref="PublishAsync"/> on the cron; it does nothing but publish one
/// <see cref="DigestAssemblyDue"/> onto the durable bus, stamping the due time from <see cref="IClock"/>.
/// The digest is normally assembled early, when the pipeline finishes on <c>RankingCompleted</c>; this
/// tick is the backstop that assembles whatever the day has produced by 06:45 — a Partial digest for a
/// still-running Run, a reduced one for a <c>CostAborted</c> Run — so the 07:00 slot always has a digest
/// to deliver rather than nothing (AC-06). Assembly is idempotent on the unique <c>run_id</c>, so a tick
/// that fires after an early assembly is a harmless no-op.
/// </summary>
internal sealed class DigestAssemblyTrigger(IMessageBus bus, IClock clock, ILogger<DigestAssemblyTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<DigestAssemblyTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var dueAt = _clock.UtcNow;
        _logger.LogInformation("Publishing digest assembly deadline for {DueAt:o}.", dueAt);
        await _bus.PublishAsync(new DigestAssemblyDue(dueAt)).ConfigureAwait(false);
    }
}
