using System.Diagnostics.CodeAnalysis;
using JobHunter.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Infrastructure.Messaging;

/// <summary>
/// Registers the RabbitMQ dead-letter replayer behind its port (T16). Kept separate from the main
/// Infrastructure composition because only the Worker CLI needs it. Excluded from coverage — wiring.
/// </summary>
[ExcludeFromCodeCoverage]
public static class DeadLetterReplayRegistration
{
    public static IServiceCollection AddDeadLetterReplay(this IServiceCollection services) =>
        services.AddSingleton<IDeadLetterReplayer, RabbitMqDeadLetterReplayer>();
}
