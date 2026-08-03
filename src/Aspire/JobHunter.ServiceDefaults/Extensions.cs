using System.Diagnostics.CodeAnalysis;
using JobHunter.Application.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace JobHunter.ServiceDefaults;

/// <summary>
/// The shared platform wiring every host calls once: OpenTelemetry, health, resilience and service
/// discovery. Production code (not the AppHost), identical in every environment — only endpoint values
/// differ (SAD §10 QG-3). No <c>#if DEBUG</c>, no environment branching.
/// </summary>
[ExcludeFromCodeCoverage]
public static class Extensions
{
    private const string AlivePath = "/alive";
    private const string ReadyPath = "/ready";
    private const string HealthPath = "/health";

    /// <summary>Tag marking a health check that gates readiness (`/ready`).</summary>
    public const string ReadyTag = "ready";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(builder.Environment.ApplicationName, serviceVersion: BuildInfo.Version)
                .AddAttributes(
                [
                    new("deployment.environment", builder.Environment.EnvironmentName.ToLowerInvariant()),
                ]))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation()
                .AddMeter(Telemetry.MeterName))
            .WithTracing(t => t
                .AddSource(Telemetry.ActivitySourceName)
                .AddNpgsql()
                .AddAspNetCoreInstrumentation(o =>
                    o.Filter = ctx => ctx.Request.Path != AlivePath && ctx.Request.Path != ReadyPath)
                .AddHttpClientInstrumentation());

        // The exporter is only registered when an endpoint is configured. With it unset the app starts
        // normally; with it set-but-unreachable the exporter drops telemetry silently (AC-06).
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // `/alive` is unconditional. Dependency checks are tagged `ready` and gate `/ready` only.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps `/alive` (liveness, anonymous), `/ready` (dependency readiness, anonymous) and `/health`
    /// (full check, admin-scoped) per observability §3. `/ready` deliberately checks only the hard
    /// dependencies (PostgreSQL, RabbitMQ, Redis) — never Anthropic or Typesense.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // The liveness and readiness probes are the only anonymous endpoints (F9 T04 AC): a k8s probe
        // carries no token, and a fallback-deny authorization policy would otherwise capture them.
        // AllowAnonymous is metadata only — inert in the bus-less hosts that never add the authorization
        // middleware, correct in the API host that does.
        app.MapHealthChecks(AlivePath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        }).AllowAnonymous();

        app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(ReadyTag),
        }).AllowAnonymous();

        app.MapHealthChecks(HealthPath, new HealthCheckOptions
        {
            Predicate = _ => true,
        }).RequireAuthorization("jobhunter:admin");

        return app;
    }
}
