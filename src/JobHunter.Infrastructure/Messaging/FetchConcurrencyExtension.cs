using JobHunter.Application.Discovery;
using JobHunter.Contracts.Pipeline;
using Wolverine;
using Wolverine.RabbitMQ;
using Wolverine.Util;

namespace JobHunter.Infrastructure.Messaging;

/// <summary>
/// Bounds the <see cref="SourceFetchRequested"/> listener to the configured fan-out degree (SAD §8:
/// "degree 8, configurable"), so a discovery cycle that fans out hundreds of sources fetches at most
/// <see cref="DiscoveryOptions.FetchConcurrency"/> boards at once (AC-01). It is an
/// <see cref="IWolverineExtension"/> registered in the container (<c>AddWolverineExtension</c>), applied
/// by Wolverine at bootstrap <em>after</em> F0's <c>WolverineConfiguration</c> — so the cap is added
/// with no F0 file modified, exactly as the schedule is (T10). It targets only the fetch queue; every
/// other stage keeps its own parallelism.
/// </summary>
internal sealed class FetchConcurrencyExtension(DiscoveryOptions options) : IWolverineExtension
{
    private readonly DiscoveryOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public void Configure(WolverineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The conventional queue name is the message type name (Wolverine's RabbitMQ default). Re-declaring
        // the same queue merges with the conventionally-created listener rather than making a second one.
        var queue = WolverineMessageNaming.ToMessageTypeName(typeof(SourceFetchRequested));

        options.ListenToRabbitQueue(queue)
            .MaximumParallelMessages(_options.FetchConcurrency);
    }
}
