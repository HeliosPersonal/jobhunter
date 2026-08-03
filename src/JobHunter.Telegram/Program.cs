using JobHunter.Application;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Search;
using JobHunter.ServiceDefaults;
using JobHunter.Telegram.Search;

// The Telegram deployable is an empty host in F0 that starts healthy (SAD §5). F10 adds the bot's
// command surface and the OwnerAuthorizer allowlist; the platform wiring is already in place. F9 adds the
// /search command, which shares the API's query service (the O12 decision) and only renders differently.
var builder = WebApplication.CreateBuilder(args);

builder.AddEnvVariablesAndConfigureSecrets();
builder.AddServiceDefaults();

builder.Services.AddJobHunterApplication();
builder.Services.AddJobHunterInfrastructure(builder.Configuration);

// The Typesense read adapter behind ISearchQuery, shared with the API — one query path, one configuration.
builder.Services.AddJobHunterSearch(builder.Configuration);
builder.Services.AddScoped<SearchCommandHandler>();

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
