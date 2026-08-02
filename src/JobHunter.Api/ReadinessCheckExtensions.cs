using System.Diagnostics.CodeAnalysis;
using JobHunter.Infrastructure.Configuration;
using JobHunter.ServiceDefaults;
using RabbitMQ.Client;

namespace JobHunter.Api;

/// <summary>
/// Registers the dependency health checks that gate <c>/ready</c> (observability §3). PostgreSQL and
/// RabbitMQ are hard dependencies; Redis is probed but the system degrades to DB-backed paths without
/// it. Anthropic and Typesense are deliberately NOT probed — a slow model must not fail readiness.
/// Excluded from coverage — host composition, exercised by the API integration tests.
/// </summary>
[ExcludeFromCodeCoverage]
public static class ReadinessCheckExtensions
{
    public static WebApplicationBuilder AddReadinessChecks(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connections = builder.Configuration.GetSection(ConnectionStringOptions.SectionName)
            .Get<ConnectionStringOptions>() ?? new ConnectionStringOptions();

        var checks = builder.Services.AddHealthChecks();

        if (!string.IsNullOrWhiteSpace(connections.JobHunter))
        {
            checks.AddNpgSql(connections.JobHunter, name: "postgres", tags: [Extensions.ReadyTag]);
        }

        if (!string.IsNullOrWhiteSpace(connections.Messaging))
        {
            var amqpUri = new Uri(connections.Messaging);
            checks.AddRabbitMQ(
                async _ =>
                {
                    var factory = new ConnectionFactory { Uri = amqpUri };
                    return await factory.CreateConnectionAsync().ConfigureAwait(false);
                },
                name: "rabbitmq",
                tags: [Extensions.ReadyTag]);
        }

        if (!string.IsNullOrWhiteSpace(connections.Cache))
        {
            checks.AddRedis(connections.Cache, name: "redis", tags: [Extensions.ReadyTag]);
        }

        return builder;
    }
}
