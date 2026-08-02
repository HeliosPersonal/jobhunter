using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Smoke;

/// <summary>
/// WebApplicationFactory smoke tests over the API host (AC-06, AC-09, AC-10). These boot the real
/// composition root, so they exercise the wiring that unit tests deliberately exclude from coverage.
/// A connection string is supplied so the host can start; no live database is touched by the endpoints
/// under test (liveness is unconditional). The readiness dependency probes are covered by the
/// integration suite.
/// </summary>
public sealed class ApiSmokeTests
{
    private static WebApplicationFactory<JobHunter.Api.Program> CreateFactory(
        IDictionary<string, string?>? extra = null) =>
        new WebApplicationFactory<JobHunter.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            // UseSetting writes values that are visible the moment the host reads its configuration —
            // ConfigureAppConfiguration for a minimal-hosting Program runs too late for the composition
            // root, which reads ConnectionStrings:JobHunter at build time (AC-09 guard).
            builder.UseSetting("ConnectionStrings:JobHunter", "Host=localhost;Database=jh;Username=u;Password=p");
            builder.UseSetting("ConnectionStrings:Messaging", "amqp://guest:guest@localhost:5672");
            builder.UseSetting("Messaging:ConnectionString", "amqp://guest:guest@localhost:5672");

            if (extra is not null)
            {
                foreach (var (key, value) in extra)
                {
                    builder.UseSetting(key, value);
                }
            }
        });

    [Fact]
    public async Task Liveness_is_anonymous_and_healthy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_endpoint_is_anonymous()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/ready", UriKind.Relative));

        // Anonymous access is allowed; the body may report not-ready when dependencies are down, but the
        // endpoint must never be a 401/403 (AC-10).
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_full_health_endpoint_requires_authorization()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        // /health is admin-scoped; an anonymous caller is refused (AC-10).
        new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden }.ShouldContain(response.StatusCode);
    }

    [Fact]
    public async Task Telemetry_endpoint_unreachable_does_not_block_startup_or_liveness()
    {
        // Pointing the exporter at an unreachable collector must not stop the host or fail liveness (AC-06).
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1",
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
