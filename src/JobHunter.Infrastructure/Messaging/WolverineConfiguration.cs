using JasperFx.Resources;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace JobHunter.Infrastructure.Messaging;

/// <summary>
/// Configures Wolverine over RabbitMQ with the EF Core transactional outbox and inbox (T08). Handlers
/// are discovered by scanning <c>JobHunter.Application</c>, so a new handler needs no registration
/// (QG-1). Conventional routing derives each queue from the message type; each stage gets a dead-letter
/// queue. A redelivered message is deduplicated by the inbox before the handler runs.
/// </summary>
public static class WolverineConfiguration
{
    /// <summary>
    /// Applies the JobHunter Wolverine conventions to <paramref name="opts"/>. Kept separate from the
    /// host builder so tests can drive it against Testcontainers Postgres + RabbitMQ.
    /// </summary>
    public static void Configure(WolverineOptions opts, MessagingOptions messaging, string databaseConnectionString)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(messaging);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseConnectionString);

        opts.ApplicationAssembly = typeof(JobHunter.Application.DependencyInjection).Assembly;

        // Durable outbox + inbox in PostgreSQL; envelopes commit inside the application transaction.
        opts.PersistMessagesWithPostgresql(databaseConnectionString);
        opts.UseEntityFrameworkCoreTransactions();
        opts.Policies.UseDurableLocalQueues();
        opts.Policies.UseDurableInboxOnAllListeners();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

        // Every handler runs inside a transaction so state change and publish are atomic (AC-03).
        opts.Policies.AutoApplyTransactions();

        var rabbit = opts.UseRabbitMq(new Uri(messaging.ConnectionString));
        if (messaging.AutoProvision)
        {
            rabbit.AutoProvision();
        }

        // Conventional routing derives queue/exchange names from the message type; each stage gets a DLQ.
        rabbit.UseConventionalRouting();

        // Retry, then dead-letter. A poison message lands in its DLQ rather than blocking the queue.
        opts.Policies.OnAnyException()
            .RetryTimes(messaging.MaxDeliveryAttempts)
            .Then.MoveToErrorQueue();
    }

    /// <summary>Marker so hosts opt into resource setup (queue + table provisioning) on start.</summary>
    public static void EnableResourceSetup(WolverineOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        opts.Services.AddResourceSetupOnStartup();
    }
}
