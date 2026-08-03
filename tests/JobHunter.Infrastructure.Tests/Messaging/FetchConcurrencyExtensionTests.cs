using JobHunter.Application.Discovery;
using JobHunter.Contracts.Pipeline;
using JobHunter.Infrastructure.Messaging;
using JobHunter.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.RabbitMQ;
using Wolverine.Runtime;
using Wolverine.Util;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Messaging;

/// <summary>
/// T10, AC "concurrency never exceeds the configured degree". The <see cref="FetchConcurrencyExtension"/>
/// is the seam that caps the <see cref="SourceFetchRequested"/> listener at <c>Discovery:FetchConcurrency</c>
/// with no F0 messaging file modified. This drives the real extension through a real Wolverine bootstrap —
/// registered exactly as production does, via <c>AddWolverineExtension</c>, over a live RabbitMQ container —
/// and asserts the compiled runtime listener for the fetch queue carries the degree cap.
/// <c>MaxDegreeOfParallelism</c> is precisely "the maximum number of messages processed at one time", so a
/// cap of N there is the guarantee that no more than N boards are fetched in parallel. Asserting the
/// compiled topology rather than racing live messages keeps the test deterministic. Requires Docker.
/// </summary>
public sealed class FetchConcurrencyExtensionTests
{
    [RequiresDockerFact]
    public async Task It_caps_the_fetch_listener_at_the_configured_degree()
    {
        await using var broker = await TestBroker.CreateAsync();
        using var host = await StartHostAsync(broker, degree: 3);

        FetchQueueListener(host).MaxDegreeOfParallelism.ShouldBe(3);
    }

    [RequiresDockerFact]
    public async Task A_different_configured_degree_flows_through_to_the_listener()
    {
        await using var broker = await TestBroker.CreateAsync();
        using var host = await StartHostAsync(broker, degree: 1);

        FetchQueueListener(host).MaxDegreeOfParallelism.ShouldBe(1);
    }

    private static Endpoint FetchQueueListener(IHost host)
    {
        // The conventional queue name is the message type name — the same derivation the extension uses.
        var queue = WolverineMessageNaming.ToMessageTypeName(typeof(SourceFetchRequested));
        var runtime = (WolverineRuntime)host.Services.GetRequiredService<IWolverineRuntime>();

        var endpoint = runtime.Endpoints.ActiveListeners()
            .Select(listener => listener.Endpoint)
            .FirstOrDefault(e => string.Equals(e.EndpointName, queue, StringComparison.OrdinalIgnoreCase));

        endpoint.ShouldNotBeNull($"the extension must configure a '{queue}' listener");
        return endpoint!;
    }

    private static async Task<IHost> StartHostAsync(TestBroker broker, int degree)
    {
        var builder = Host.CreateApplicationBuilder();

        // Registered exactly as production does. The extension's dependency is the DiscoveryOptions whose
        // FetchConcurrency it reads; supply one carrying the degree under test.
        builder.Services.AddSingleton(new DiscoveryOptions { FetchConcurrency = degree });
        builder.Services.AddWolverineExtension<FetchConcurrencyExtension>();

        builder.UseWolverine(opts =>
        {
            // A live RabbitMQ transport so endpoints compile on start; the Application handler scan is left
            // off so only the extension shapes the fetch endpoint, isolating the cap under test.
            opts.UseRabbitMq(new Uri(broker.ConnectionString)).AutoProvision();
            opts.Discovery.DisableConventionalDiscovery();
        });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }
}
