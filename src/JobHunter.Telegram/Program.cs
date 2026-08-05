using JobHunter.Application;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
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

// The Typesense read adapter behind ISearchQuery, shared with the API — one query path, one configuration.
builder.Services.AddJobHunterSearch(builder.Configuration);
builder.Services.AddScoped<SearchCommandHandler>();

// The bot host: allowlist, pacer, INotifier and the long-poll hosted service (F5 T07).
builder.Services.AddJobHunterTelegramBot(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
