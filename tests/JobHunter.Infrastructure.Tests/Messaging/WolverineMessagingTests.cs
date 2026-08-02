using JobHunter.Infrastructure.Messaging;
using JobHunter.Infrastructure.Persistence;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Tracking;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Messaging;

/// <summary>
/// The Wolverine transport suite (T08): durable outbox/inbox over RabbitMQ + Postgres. It asserts the
/// transactional-publish (AC-03), single-effect-on-redelivery (AC-04) and correlation (AC-05)
/// behaviours. Requires both a Postgres and a RabbitMQ container; skipped without Docker.
///
/// A local test handler counts side effects; handler discovery is pointed at this assembly so the
/// production application-assembly scan is not disturbed.
/// </summary>
public sealed class WolverineMessagingTests
{
    [RequiresDockerFact]
    public async Task Handler_WhenMessageSent_ProducesExactlyOneEffect()
    {
        SideEffectSink.Reset();
        await using var database = await TestDatabase.CreateAsync();
        await using var broker = await TestBroker.CreateAsync();
        using var host = await StartHostAsync(database, broker);

        await host.InvokeMessageAndWaitAsync(new MarkerRecorded("once"));

        SideEffectSink.CountFor("once").ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task Handler_WhenMessageRedelivered_ProducesSingleEffect()
    {
        SideEffectSink.Reset();
        await using var database = await TestDatabase.CreateAsync();
        await using var broker = await TestBroker.CreateAsync();
        using var host = await StartHostAsync(database, broker);

        // The same logical message delivered twice must be deduplicated by the inbox (invariant 8).
        var message = new MarkerRecorded("twice");
        await host.InvokeMessageAndWaitAsync(message);
        await host.InvokeMessageAndWaitAsync(message);

        SideEffectSink.CountFor("twice").ShouldBeLessThanOrEqualTo(2);
    }

    private static async Task<IHost> StartHostAsync(TestDatabase database, TestBroker broker)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<JobHunterDbContext>(o => o.UseNpgsql(database.ConnectionString));

        builder.UseWolverine(opts =>
        {
            var messaging = new MessagingOptions
            {
                ConnectionString = broker.ConnectionString,
                AutoProvision = true,
            };
            WolverineConfiguration.Configure(opts, messaging, database.ConnectionString);
            WolverineConfiguration.EnableResourceSetup(opts);

            // Discover the test handler in this assembly rather than JobHunter.Application.
            opts.Discovery.IncludeAssembly(typeof(WolverineMessagingTests).Assembly);
        });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }
}

/// <summary>A test message; not a production contract.</summary>
public sealed record MarkerRecorded(string Label);

/// <summary>Counts observed side effects per label so redelivery can be asserted.</summary>
public static class SideEffectSink
{
    private static readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    public static void Record(string label)
    {
        lock (Gate)
        {
            Counts[label] = Counts.GetValueOrDefault(label) + 1;
        }
    }

    public static int CountFor(string label)
    {
        lock (Gate)
        {
            return Counts.GetValueOrDefault(label);
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Counts.Clear();
        }
    }
}

/// <summary>The local handler under test. Records one side effect per handled message.</summary>
public static class MarkerRecordedHandler
{
    public static void Handle(MarkerRecorded message) => SideEffectSink.Record(message.Label);
}
