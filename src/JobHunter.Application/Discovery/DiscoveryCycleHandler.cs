using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Discovery;

/// <summary>
/// Opens one discovery cycle (SAD §6.1). It reads the sources due this window — active companies with a
/// live, confident, non-quarantined binding not already fetched this window — and publishes exactly one
/// <see cref="SourceFetchRequested"/> per source. One message per source is the QG-1 isolation boundary:
/// a single provider's failure is a single message's failure, not the cycle's.
///
/// The cycle itself does no fetching and holds no per-source concurrency; RabbitMQ carries the fan-out
/// and <see cref="FetchSourceHandler"/> processes the messages with the bounded degree. Idempotency is
/// structural: every fan-out message keys on <c>(SourceId, WindowStart)</c>, so a cycle that runs twice
/// for the same window (a redelivered tick, two overlapping cycles) produces the same keys and the inbox
/// deduplicates the fetch (invariant 8).
/// </summary>
public sealed class DiscoveryCycleHandler(
    IDiscoveryCycleQuery dueSources,
    IClock clock,
    ILogger<DiscoveryCycleHandler> logger)
{
    private readonly IDiscoveryCycleQuery _dueSources = dueSources ?? throw new ArgumentNullException(nameof(dueSources));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<DiscoveryCycleHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(
        DiscoveryCycleDue message,
        IMessageBus bus,
        DiscoveryOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(options);

        var now = _clock.UtcNow;

        // Skip a source already fetched within the recent-refetch window: an overlapping cycle fetches
        // it once. WindowStart is the cycle's own instant, so redelivery of this tick reads the same set.
        var fetchedBefore = message.WindowStart - options.RecentFetchWindow;
        var due = await _dueSources.DueSourcesAsync(now, fetchedBefore, cancellationToken).ConfigureAwait(false);

        foreach (var source in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await bus.PublishAsync(new SourceFetchRequested(
                source.SourceId, source.CompanyId, source.AtsKind, message.WindowStart, now))
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Discovery cycle for window {WindowStart:o} fanned out {Count} source fetch request(s).",
            message.WindowStart, due.Count);
    }
}
