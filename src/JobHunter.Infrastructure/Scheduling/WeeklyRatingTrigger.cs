using JobHunter.Application.Ratings;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The Hangfire job body for the weekly precision@10 rating prompt (F4 T20 done-when 1 and 5). Hangfire invokes
/// <see cref="PublishAsync"/> on the Monday 09:00 Europe/Kyiv cron — its own message, well clear of the 07:00
/// digest and 08:00 reminder, so the "rate last week's cards" prompt never crowds the morning opportunities.
/// It does nothing but publish one <see cref="WeeklyRatingDue"/> onto the durable bus, stamping the due instant
/// from <see cref="IClock"/>. Kept this thin so the loop's own logic (open the round once, load the week's
/// top-ten, prompt each) lives in the <see cref="WeeklyRatingHandler"/> Application handler, unit-tested
/// without Hangfire.
///
/// <para>Stamping the instant here (not in the handler) is what makes a redelivered tick read the same
/// seven-day window and open the same round, so the ratings — and therefore precision@10 — are never
/// double-counted (done-when 5).</para>
/// </summary>
internal sealed class WeeklyRatingTrigger(IMessageBus bus, IClock clock, ILogger<WeeklyRatingTrigger> logger)
{
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<WeeklyRatingTrigger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PublishAsync()
    {
        var dueAt = _clock.UtcNow;
        _logger.LogInformation("Publishing weekly rating tick for instant {DueAt:o}.", dueAt);
        await _bus.PublishAsync(new WeeklyRatingDue(dueAt)).ConfigureAwait(false);
    }
}
