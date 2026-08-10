---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "06-07"
ticket: ""
tags: [engineering, local-dev, jobhunter]
---

# Local development

> One command: `dotnet run --project src/Aspire/JobHunter.AppHost`.
> Everything below explains what that command does and how to work without it.

---

## 1. Prerequisites

| Tool | Version | Why |
|---|---|---|
| .NET SDK | 10.0.302+ | `net10.0` target |
| Aspire workload | `dotnet workload install aspire` | AppHost orchestration |
| Docker Desktop / Colima | any current | Aspire containers + Testcontainers |
| `dotnet-ef` | 10.x | migrations |

No PostgreSQL, RabbitMQ, Redis or Typesense installation is needed — Aspire provisions all of them
as containers.

---

## 2. First run

```bash
git clone git@github.com:<owner>/jobhunter.git && cd jobhunter

# secrets that cannot be faked — stored in user-secrets, never in the repo
dotnet user-secrets --project src/Aspire/JobHunter.AppHost set "Anthropic:ApiKey"    "sk-ant-..."
dotnet user-secrets --project src/Aspire/JobHunter.AppHost set "Telegram:BotToken"   "1234:ABC..."
dotnet user-secrets --project src/Aspire/JobHunter.AppHost set "Telegram:OwnerChatId" "123456789"

dotnet run --project src/Aspire/JobHunter.AppHost
```

The Aspire dashboard opens at `https://localhost:17090` with live logs, traces and metrics for every
resource. Migrations are applied automatically on first start in Development.

Without an Anthropic key the system still runs: the LLM tier falls back to the Aspire-provisioned
Ollama container ([[../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]]),
so discovery, normalisation, dedup, ranking and delivery are all exercisable offline. Only
enrichment and matching quality degrade.

---

## 3. What the AppHost provisions

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgWeb();
var jobhunterDb = postgres.AddDatabase("jobhunterdb");

var rabbit = builder.AddRabbitMQ("messaging").WithManagementPlugin();
var redis  = builder.AddRedis("cache").WithRedisInsight();

var typesense = builder.AddContainer("typesense", "typesense/typesense", "27.1")
    .WithHttpEndpoint(port: 8108, targetPort: 8108, name: "http")
    .WithArgs("--data-dir", "/data", "--api-key", "dev-typesense-key");

var ollama = builder.AddContainer("ollama", "ollama/ollama", "latest")
    .WithHttpEndpoint(port: 11434, targetPort: 11434, name: "http")
    .WithVolume("ollama-models", "/root/.ollama");

var worker = builder.AddProject<Projects.JobHunter_Worker>("worker")
    .WithReference(jobhunterDb).WaitFor(jobhunterDb)
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(redis)
    .WithEnvironment("Typesense__Url", typesense.GetEndpoint("http"))
    .WithEnvironment("Ollama__Url", ollama.GetEndpoint("http"));

builder.AddProject<Projects.JobHunter_Api>("api")
    .WithReference(jobhunterDb).WaitFor(jobhunterDb)
    .WithReference(redis)
    .WithEnvironment("Typesense__Url", typesense.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.JobHunter_Telegram>("telegram")
    .WithReference(jobhunterDb).WaitFor(jobhunterDb)
    .WithReference(rabbit).WaitFor(rabbit);

builder.Build().Run();
```

| Resource | Local endpoint |
|---|---|
| Aspire dashboard | `https://localhost:17090` |
| API + Scalar/OpenAPI | `https://localhost:7001/scalar` |
| pgweb | via the dashboard |
| RabbitMQ management | via the dashboard, `guest`/`guest` |
| RedisInsight | via the dashboard |
| Typesense | `http://localhost:8108` |

---

## 4. Everyday commands

```bash
# build + full hermetic test suite (needs Docker)
dotnet build && dotnet test

# collect coverage locally, the same collector CI uses
dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings
# the > 90% line+branch gate itself is enforced by the "Enforce coverage gate" step in CI,
# which merges the per-assembly cobertura reports and fails the build below threshold

# add a migration — always name it <Feature>_<What>
dotnet ef migrations add F2_AddJobsAndAliases \
  --project src/JobHunter.Infrastructure \
  --startup-project src/JobHunter.Worker \
  --output-dir Persistence/Migrations

# inspect the SQL a migration will emit, before trusting it
dotnet ef migrations script --idempotent \
  --project src/JobHunter.Infrastructure --startup-project src/JobHunter.Worker

# run one stage by hand against local data
dotnet run --project src/JobHunter.Worker -- run-stage discovery --company stripe.com
dotnet run --project src/JobHunter.Worker -- run-once --dry-run   # full Run, no LLM calls, no delivery
```

`--dry-run` executes the whole pipeline with a stub `ILlmBatchClient` that replays fixtures and a
stub `INotifier` that writes the digest to stdout. It is the fastest way to see an end-to-end change.

---

## 5. Seed data

```bash
dotnet run --project src/JobHunter.Worker -- seed --companies tools/seed/companies.yaml
```

`tools/seed/companies.yaml` holds ~50 companies with known-good ATS bindings, so discovery produces
real data on the first run without waiting for detection. The full ~300-company list is F1 T3.

---

## 6. Without Aspire

`compose.yaml` provides the same four backing services for contributors who cannot install the
workload. It is a fallback, not the documented path
([[../00-overview/adr/0013-aspire-local-dev-only|ADR-0013]]):

```bash
docker compose up -d postgres rabbitmq redis typesense
export ConnectionStrings__JobHunter="Host=localhost;Database=jobhunter;Username=postgres;Password=postgres"
export ConnectionStrings__Messaging="amqp://guest:guest@localhost:5672/"
dotnet run --project src/JobHunter.Worker
```

---

## 7. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| AppHost exits immediately | Docker not running | Start Docker, retry |
| Integration tests fail to start | Testcontainers cannot reach the socket | `export DOCKER_HOST=unix://$HOME/.colima/default/docker.sock` (Colima) |
| Migrations fail on start | A migration was edited after being applied | `dotnet ef database drop -f` then rerun — safe locally, never elsewhere |
| No jobs after a discovery run | Empty company registry | Run the seed command in §5 |
| Enrichment produces nothing | No Anthropic key and Ollama has no model | `docker exec -it ollama ollama pull llama3.1:8b` |
| Bot does not respond | `Telegram:OwnerChatId` mismatch | Send `/start`, read the rejected chat id from the warning log, set it |
| Port 8108 in use | A stray Typesense container | `docker rm -f typesense` |

---

## Related

- [[coding-standards]] · [[testing-strategy]] · [[deployment]]
- [[../00-overview/adr/0013-aspire-local-dev-only|ADR-0013]]
