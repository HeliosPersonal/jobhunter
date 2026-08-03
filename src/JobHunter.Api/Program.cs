using JobHunter.Api;
using JobHunter.Api.Endpoints;
using JobHunter.Application;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Search;
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
builder.Services.AddJobHunterSearch(builder.Configuration);

// 4. Keycloak OIDC bearer auth for the API surface; the admin scope gates /health and future ops.
builder.AddApiSecurity();

// 5. Problem details, OpenAPI + Scalar and per-token rate limiting (T04).
builder.AddApiPipeline();

// 6. Readiness dependency checks — tagged `ready`, gate /ready only. PostgreSQL and RabbitMQ are hard
//    dependencies; Redis degrades gracefully but is still probed (observability §3).
builder.AddReadinessChecks();

var app = builder.Build();

app.UseApiPipeline();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

// F9 read surface (T05): search, job detail, aliases and the recent-jobs list. Each route declares its
// jobhunter:read scope explicitly (endpoint-convention gate).
app.MapSearchEndpoints();
app.MapJobEndpoints();
app.MapCompanyEndpoints();

await app.RunAsync();

namespace JobHunter.Api
{
    /// <summary>Exposed only so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in tests.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed partial class Program;
}
