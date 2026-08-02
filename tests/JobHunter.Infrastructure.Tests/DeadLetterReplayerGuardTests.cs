using JobHunter.Application.Messaging;
using JobHunter.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests;

/// <summary>
/// Broker-free tests for the paths in the RabbitMQ replayer that decide an outcome before ever opening a
/// connection: a queue whose name is not a dead-letter queue, and <c>--list</c> without a management URL.
/// The AMQP drain path itself is exercised by the Testcontainers messaging suite (Docker required).
/// </summary>
public sealed class DeadLetterReplayerGuardTests
{
    private static IDeadLetterReplayer CreateReplayer(MessagingOptions options)
    {
        // The adapter is internal; reach it through the public registration + the port.
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDeadLetterReplay();
        return services.BuildServiceProvider().GetRequiredService<IDeadLetterReplayer>();
    }

    [Fact]
    public async Task Replaying_a_queue_that_is_not_a_dlq_is_refused_without_touching_the_broker()
    {
        var replayer = CreateReplayer(new MessagingOptions
        {
            ConnectionString = "amqp://guest:guest@localhost:5672",
            DeadLetterSuffix = ".dlq",
        });

        var result = await replayer.ReplayQueueAsync("orders");

        result.Outcome.ShouldBe(ReplayOutcome.UnknownQueue);
        result.MovedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Listing_without_a_management_url_returns_an_empty_set()
    {
        var replayer = CreateReplayer(new MessagingOptions
        {
            ConnectionString = "amqp://guest:guest@localhost:5672",
            ManagementUrl = null,
        });

        var summaries = await replayer.ListAsync();

        summaries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Replaying_a_blank_queue_name_is_rejected()
    {
        var replayer = CreateReplayer(new MessagingOptions
        {
            ConnectionString = "amqp://guest:guest@localhost:5672",
        });

        await Should.ThrowAsync<ArgumentException>(() => replayer.ReplayQueueAsync("  "));
    }
}
