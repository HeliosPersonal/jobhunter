using JobHunter.Application;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Configuration;
using JobHunter.ServiceDefaults;

// The Telegram deployable is an empty host in F0 that starts healthy (SAD §5). F10 adds the bot's
// command surface and the OwnerAuthorizer allowlist; the platform wiring is already in place.
var builder = WebApplication.CreateBuilder(args);

builder.AddEnvVariablesAndConfigureSecrets();
builder.AddServiceDefaults();

builder.Services.AddJobHunterApplication();
builder.Services.AddJobHunterInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
