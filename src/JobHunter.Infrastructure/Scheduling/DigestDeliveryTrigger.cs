using JobHunter.Application.Delivery;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the 07:00 Europe/Kyiv delivery slot (F5 SAD §6.3, QG-1, ADR-F5-0001).
/// Hangfire invokes <see cref="PublishAsync"/> on the cron; it does nothing but publish one
/// <see cref="DigestDeliveryDue"/> onto the durable bus, stamping the due time from <see cref="IClock"/>.
/// 07:00 is a hard commitment: the digest is assembled earlier and held, and this tick is the only thing
/// that releases it to the Owner, so nothing lands before the slot and the digest lands on it. Delivery
/// is idempotent per card (invariant 8), so a redelivered tick re-sends nothing.
/// </summary>
internal sealed class DigestDeliveryTrigger(IMessageBus bus, IClock clock, ILogger<DigestDeliveryTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<DigestDeliveryTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var dueAt = _clock.UtcNow;
        _logger.LogInformation("Publishing digest delivery slot for {DueAt:o}.", dueAt);
        await _bus.PublishAsync(new DigestDeliveryDue(dueAt)).ConfigureAwait(false);
    }
}
