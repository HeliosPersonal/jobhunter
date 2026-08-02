using Testcontainers.RabbitMq;

namespace JobHunter.TestKit;

/// <summary>
/// A single RabbitMQ container for a messaging test, exposing its AMQP connection string. Paired with
/// <see cref="TestDatabase"/> for the Wolverine outbox/inbox suites. Disposed with the test. Requires a
/// Docker engine — the messaging suites are gated by <see cref="RequiresDockerFactAttribute"/>.
/// </summary>
public sealed class TestBroker : IAsyncDisposable
{
    private readonly RabbitMqContainer _container;

    private TestBroker(RabbitMqContainer container) => _container = container;

    /// <summary>The AMQP URI, e.g. <c>amqp://guest:guest@localhost:32768</c>.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public static async Task<TestBroker> CreateAsync()
    {
        var container = new RabbitMqBuilder("rabbitmq:4-management").Build();
        await container.StartAsync().ConfigureAwait(false);
        return new TestBroker(container);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
