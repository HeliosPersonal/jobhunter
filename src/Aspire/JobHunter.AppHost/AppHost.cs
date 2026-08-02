// JobHunter local-development orchestration (ADR-0013). This runs only on a developer's machine and is
// never containerised or deployed. It provisions the backing services, declares the jobhunterdb
// database, and wires the three hosts by resource name so no host hard-codes a local endpoint (AC-01).

var builder = DistributedApplication.CreateBuilder(args);

// --- Backing services ---
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgWeb();

var jobhunterdb = postgres.AddDatabase("jobhunterdb");

var messaging = builder.AddRabbitMQ("messaging")
    .WithDataVolume()
    .WithManagementPlugin();

var cache = builder.AddRedis("cache")
    .WithRedisInsight();

var typesense = builder.AddContainer("typesense", "typesense/typesense", "27.1")
    .WithHttpEndpoint(port: 8108, targetPort: 8108, name: "http")
    .WithEnvironment("TYPESENSE_API_KEY", "local-dev-key")
    .WithEnvironment("TYPESENSE_DATA_DIR", "/data")
    .WithArgs("--data-dir", "/data", "--api-key", "local-dev-key", "--enable-cors");

var ollama = builder.AddContainer("ollama", "ollama/ollama", "latest")
    .WithHttpEndpoint(port: 11434, targetPort: 11434, name: "http");

// --- Application hosts ---
builder.AddProject<Projects.JobHunter_Api>("api")
    .WithReference(jobhunterdb).WaitFor(jobhunterdb)
    .WithReference(messaging).WaitFor(messaging)
    .WithReference(cache).WaitFor(cache);

builder.AddProject<Projects.JobHunter_Worker>("worker")
    .WithReference(jobhunterdb).WaitFor(jobhunterdb)
    .WithReference(messaging).WaitFor(messaging)
    .WithReference(cache).WaitFor(cache)
    .WaitFor(typesense)
    .WaitFor(ollama);

builder.AddProject<Projects.JobHunter_Telegram>("telegram")
    .WithReference(jobhunterdb).WaitFor(jobhunterdb)
    .WithReference(messaging).WaitFor(messaging);

builder.Build().Run();
