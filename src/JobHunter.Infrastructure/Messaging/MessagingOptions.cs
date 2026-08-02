namespace JobHunter.Infrastructure.Messaging;

/// <summary>
/// Wolverine + RabbitMQ options (T08). Conventional routing names each queue
/// <c>{MessageType.FullName}.{consuming-deployable}</c>; each stage gets its own dead-letter queue so a
/// poison message lands there rather than blocking the queue. The EF Core transactional outbox and
/// inbox make state change and event publication atomic (ADR-0002, ADR-0007).
/// </summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>The AMQP connection URI. Required.</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>The consuming deployable's suffix, e.g. <c>jobhunter-worker</c>.</summary>
    public string ServiceName { get; init; } = "jobhunter-worker";

    /// <summary>Let Wolverine declare exchanges, queues and bindings on start (local + staging).</summary>
    public bool AutoProvision { get; init; } = true;

    /// <summary>Times a failed message is retried before it is dead-lettered.</summary>
    public int MaxDeliveryAttempts { get; init; } = 3;

    /// <summary>
    /// The suffix that marks a dead-letter queue. The <c>replay-dlq</c> CLI derives a source queue by
    /// stripping this suffix (T16). A queue that does not end with it is not treated as a DLQ.
    /// </summary>
    public string DeadLetterSuffix { get; init; } = ".dlq";

    /// <summary>
    /// The RabbitMQ management HTTP base URL, e.g. <c>http://localhost:15672</c>. Used only by the
    /// <c>replay-dlq --list</c> command to enumerate queues; AMQP cannot list them. Optional.
    /// </summary>
    public string? ManagementUrl { get; init; }

    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            error = $"{SectionName}:{nameof(ConnectionString)} is required.";
            return false;
        }

        error = null;
        return true;
    }
}
