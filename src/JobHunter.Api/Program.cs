using JobHunter.Api;
using JobHunter.Api.Endpoints;
using JobHunter.Application;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Scheduling;
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

// 3a. Hangfire client-only storage so the operational endpoints (T07) can enqueue a reindex or a reprocess
//     that the Worker's background server runs (ADR-0004). EnableServer stays false — the Api never runs a
//     server — and schema preparation is skipped so no connection is opened at boot (the migrator Job owns
//     the schema). The IBackgroundJobClient this registers backs the HangfireOperationScheduler.
var hangfire = builder.Configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>()
               ?? new HangfireOptions();
var hangfireConnection = builder.Configuration.GetConnectionString("JobHunter")
                         ?? throw new InvalidOperationException("ConnectionStrings:JobHunter is required.");
builder.Services.AddJobHunterHangfire(
    new HangfireOptions { EnableServer = false, SchemaName = hangfire.SchemaName },
    hangfireConnection,
    prepareSchema: false);

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

// F4 CV upload (T03): the owner-scoped endpoint the Owner uploads a new CV through — the one place a CV
// enters the system. Declares jobhunter:read, which the scope-plus-Owner policy gates to the Owner alone.
app.MapCvEndpoints();

// F9 operational surface (T07): reindex, source release, reprocess and corpus stats. Each route declares
// its jobhunter:admin scope explicitly (endpoint-convention gate) so recovery never needs database access.
app.MapAdminEndpoints();

// F6 application tracking (T09): the pipeline, one application's history, the two owner writes (status and
// note) and the what-needs-attention read. The reads declare jobhunter:read, the writes jobhunter:admin.
app.MapApplicationEndpoints();

await app.RunAsync();

namespace JobHunter.Api
{
    /// <summary>Exposed only so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in tests.</summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed partial class Program;
}
