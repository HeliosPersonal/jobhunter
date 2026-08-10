using JobHunter.Application;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Scheduling;
using JobHunter.Search;
using JobHunter.ServiceDefaults;
using JobHunter.Telegram;
using JobHunter.Telegram.Search;

// The Telegram deployable. F0 started it as an empty healthy host; F9 added the /search command sharing the
// API's query service (the O12 decision); F5 T07 adds the bot itself — the OwnerAuthorizer allowlist, the
// paced INotifier and the single-replica long-poll loop (SAD §7). One replica by design (Recreate).
var builder = WebApplication.CreateBuilder(args);

builder.AddEnvVariablesAndConfigureSecrets();
builder.AddServiceDefaults();

builder.Services.AddJobHunterApplication();
builder.Services.AddJobHunterInfrastructure(builder.Configuration);

// Hangfire client-only storage so /run and /redeliver can enqueue the daily-run and delivery triggers that the
// Worker's background server runs (ADR-0004) — the bus-less Telegram host reaches Worker-side work the same way
// the Api's operational endpoints do. EnableServer stays false and schema preparation is skipped so no connection
// is opened at boot (the migrator Job owns the schema). The IBackgroundJobClient this registers backs the
// HangfireOperationScheduler that Infrastructure already binds to IOperationScheduler.
var hangfire = builder.Configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>()
               ?? new HangfireOptions();
var hangfireConnection = builder.Configuration.GetConnectionString("JobHunter")
                         ?? throw new InvalidOperationException("ConnectionStrings:JobHunter is required.");
builder.Services.AddJobHunterHangfire(
    new HangfireOptions { EnableServer = false, SchemaName = hangfire.SchemaName },
    hangfireConnection,
    prepareSchema: false);

// The Typesense read adapter behind ISearchQuery, shared with the API — one query path, one configuration.
builder.Services.AddJobHunterSearch(builder.Configuration);
builder.Services.AddScoped<SearchCommandHandler>();

// The bot host: allowlist, pacer, INotifier and the long-poll hosted service (F5 T07).
builder.Services.AddJobHunterTelegramBot(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
