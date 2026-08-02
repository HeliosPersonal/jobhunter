using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobHunter.Application.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace JobHunter.Infrastructure.Messaging;

/// <summary>
/// The RabbitMQ adapter behind <see cref="IDeadLetterReplayer"/> (T16). Listing uses the management HTTP
/// API (AMQP cannot enumerate queues); replay drains the dead-letter queue over AMQP and republishes to
/// the default exchange keyed on the source queue name, acking each message only after it is confirmed
/// re-enqueued. A crash mid-replay leaves un-acked messages on the DLQ — at-least-once, never a loss.
/// Internal: reached only through the port (architecture rule 8).
/// </summary>
internal sealed class RabbitMqDeadLetterReplayer : IDeadLetterReplayer
{
    private readonly MessagingOptions _options;
    private readonly ILogger<RabbitMqDeadLetterReplayer> _logger;

    public RabbitMqDeadLetterReplayer(
        IOptions<MessagingOptions> options,
        ILogger<RabbitMqDeadLetterReplayer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<DeadLetterSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ManagementUrl))
        {
            _logger.LogWarning("replay-dlq --list needs Messaging:ManagementUrl; none configured.");
            return [];
        }

        var amqp = new Uri(_options.ConnectionString);
        using var client = CreateManagementClient(amqp);

        var vhost = string.IsNullOrEmpty(amqp.AbsolutePath.Trim('/')) ? "%2F" : Uri.EscapeDataString(amqp.AbsolutePath.Trim('/'));
        var queues = await client
            .GetFromJsonAsync<IReadOnlyList<ManagementQueue>>($"/api/queues/{vhost}", cancellationToken)
            .ConfigureAwait(false) ?? [];

        return queues
            .Where(q => q.Name.EndsWith(_options.DeadLetterSuffix, StringComparison.Ordinal))
            .Select(q => new DeadLetterSummary(q.Name, SourceQueueOf(q.Name), q.Messages))
            .OrderByDescending(s => s.MessageCount)
            .ToList();
    }

    public async Task<ReplayResult> ReplayQueueAsync(
        string deadLetterQueue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deadLetterQueue);

        if (!deadLetterQueue.EndsWith(_options.DeadLetterSuffix, StringComparison.Ordinal))
        {
            return ReplayResult.UnknownQueue(deadLetterQueue);
        }

        var factory = new ConnectionFactory { Uri = new Uri(_options.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await connection
            .CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        uint depth;
        try
        {
            var declare = await channel.QueueDeclarePassiveAsync(deadLetterQueue, cancellationToken)
                .ConfigureAwait(false);
            depth = declare.MessageCount;
        }
        catch (OperationInterruptedException)
        {
            return ReplayResult.UnknownQueue(deadLetterQueue);
        }

        if (depth == 0)
        {
            return ReplayResult.Empty(deadLetterQueue);
        }

        var sourceQueue = SourceQueueOf(deadLetterQueue);
        var moved = 0;
        for (var i = 0; i < depth; i++)
        {
            var result = await channel.BasicGetAsync(deadLetterQueue, autoAck: false, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                break;
            }

            // Republish to the default exchange with the source queue as the routing key, preserving
            // headers so Wolverine's inbox can deduplicate a message that was already processed.
            var properties = new BasicProperties(result.BasicProperties);
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: sourceQueue,
                mandatory: false,
                basicProperties: properties,
                body: result.Body,
                cancellationToken).ConfigureAwait(false);

            await channel.BasicAckAsync(result.DeliveryTag, multiple: false, cancellationToken)
                .ConfigureAwait(false);
            moved++;
        }

        _logger.LogInformation(
            "Replayed {MovedCount} message(s) from {DeadLetterQueue} to {SourceQueue}.",
            moved, deadLetterQueue, sourceQueue);

        return ReplayResult.Moved(moved);
    }

    private string SourceQueueOf(string deadLetterQueue) =>
        deadLetterQueue[..^_options.DeadLetterSuffix.Length];

    private static HttpClient CreateManagementClient(Uri amqp)
    {
        var userInfo = amqp.UserInfo.Split(':', 2);
        var user = userInfo.Length > 0 && userInfo[0].Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "guest";
        var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "guest";

        var client = new HttpClient { BaseAddress = new Uri(ManagementBaseUrl(amqp)) };
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}"));
        client.DefaultRequestHeaders.Authorization = new("Basic", token);
        return client;
    }

    private static string ManagementBaseUrl(Uri amqp)
    {
        // Default RabbitMQ management port is 15672; derive it from the AMQP host.
        var scheme = string.Equals(amqp.Scheme, "amqps", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        return string.Create(CultureInfo.InvariantCulture, $"{scheme}://{amqp.Host}:15672");
    }

    private sealed record ManagementQueue(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("messages")] uint Messages);
}
