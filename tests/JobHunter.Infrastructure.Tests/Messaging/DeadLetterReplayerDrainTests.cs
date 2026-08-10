using System.Text;
using JobHunter.Application.Messaging;
using JobHunter.Infrastructure.Messaging;
using JobHunter.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Messaging;

/// <summary>
/// The AMQP drain path of the RabbitMQ replayer (T16, runbook R6) against a real broker. The guard arms
/// — a non-DLQ name and a blank name — are covered broker-free elsewhere; this suite drives the arms that
/// only a live queue can reach: a queue that does not exist is reported (not thrown) via the passive
/// declare, an existing-but-empty queue is a no-op, and a queue with messages is drained back onto its
/// source queue message-for-message with each delivery acked only after it is re-enqueued (at-least-once).
/// Requires Docker.
/// </summary>
public sealed class DeadLetterReplayerDrainTests
{
    private const string Suffix = ".dlq";

    [RequiresDockerFact]
    public async Task An_existing_dlq_with_messages_is_drained_back_onto_its_source_queue()
    {
        await using var broker = await TestBroker.CreateAsync();
        const string source = "orders";
        const string dlq = "orders" + Suffix;
        await DeclareQueueAsync(broker, source);
        await DeclareQueueAsync(broker, dlq);
        await PublishAsync(broker, dlq, "a", "b", "c");

        var replayer = CreateReplayer(broker);
        var result = await replayer.ReplayQueueAsync(dlq);

        result.Outcome.ShouldBe(ReplayOutcome.Replayed);
        result.MovedCount.ShouldBe(3);
        (await DepthAsync(broker, dlq)).ShouldBe(0u);
        (await DepthAsync(broker, source)).ShouldBe(3u);
    }

    [RequiresDockerFact]
    public async Task An_existing_but_empty_dlq_is_a_no_op()
    {
        await using var broker = await TestBroker.CreateAsync();
        const string dlq = "empty" + Suffix;
        await DeclareQueueAsync(broker, dlq);

        var result = await CreateReplayer(broker).ReplayQueueAsync(dlq);

        result.Outcome.ShouldBe(ReplayOutcome.EmptyQueue);
        result.MovedCount.ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task A_dlq_name_that_does_not_exist_is_reported_not_thrown()
    {
        await using var broker = await TestBroker.CreateAsync();

        var result = await CreateReplayer(broker).ReplayQueueAsync("never-declared" + Suffix);

        result.Outcome.ShouldBe(ReplayOutcome.UnknownQueue);
        result.MovedCount.ShouldBe(0);
    }

    private static IDeadLetterReplayer CreateReplayer(TestBroker broker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new MessagingOptions
        {
            ConnectionString = broker.ConnectionString,
            DeadLetterSuffix = Suffix,
        }));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDeadLetterReplay();
        return services.BuildServiceProvider().GetRequiredService<IDeadLetterReplayer>();
    }

    private static async Task DeclareQueueAsync(TestBroker broker, string queue)
    {
        await using var connection = await ConnectAsync(broker);
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
    }

    private static async Task PublishAsync(TestBroker broker, string queue, params string[] bodies)
    {
        await using var connection = await ConnectAsync(broker);
        await using var channel = await connection.CreateChannelAsync();
        foreach (var body in bodies)
        {
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: queue,
                body: Encoding.UTF8.GetBytes(body));
        }
    }

    private static async Task<uint> DepthAsync(TestBroker broker, string queue)
    {
        await using var connection = await ConnectAsync(broker);
        await using var channel = await connection.CreateChannelAsync();
        var declare = await channel.QueueDeclarePassiveAsync(queue);
        return declare.MessageCount;
    }

    private static async Task<IConnection> ConnectAsync(TestBroker broker)
    {
        var factory = new ConnectionFactory { Uri = new Uri(broker.ConnectionString) };
        return await factory.CreateConnectionAsync();
    }
}
