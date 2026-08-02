using JobHunter.Api;
using JobHunter.Application;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
using JobHunter.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// 1. Secrets first: Development uses Aspire-injected config; Staging/Production fail fast if the
//    Infisical identity is incomplete (SAD §6.1, AC-09).
builder.AddEnvVariablesAndConfigureSecrets();

// 2. Platform defaults: OpenTelemetry, health, resilience, service discovery.
builder.AddServiceDefaults();

// 3. Application + Infrastructure composition — one extension method each (S2).
builder.Services.AddJobHunterApplication();
builder.Services.AddJobHunterInfrastructure(builder.Configuration);

// 4. Keycloak OIDC bearer auth for the API surface; the admin scope gates /health and future ops.
builder.AddApiSecurity();

// 5. Readiness dependency checks — tagged `ready`, gate /ready only. PostgreSQL and RabbitMQ are hard
//    dependencies; Redis degrades gracefully but is still probed (observability §3).
builder.AddReadinessChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

await app.RunAsync();

namespace JobHunter.Api
{
    /// <summary>Exposed only so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in tests.</summary>
    public sealed partial class Program;
}
