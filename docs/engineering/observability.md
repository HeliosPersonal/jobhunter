---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "06-07"
ticket: ""
tags: [engineering, observability, jobhunter]
---

# Observability

> The dominant failure mode of this system is **silence**: a stage that stops publishing, a batch
> that never completes, a source that quietly returns zero jobs. Everything here exists to make
> silence loud.
> Pipeline: OTLP → Grafana Alloy → Grafana Cloud
> ([[../00-overview/adr/0012-otlp-alloy-grafana-cloud|ADR-0012]]).

---

## 1. Wiring

Every host calls `builder.AddServiceDefaults()`:

```csharp
public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
    where TBuilder : IHostApplicationBuilder
{
    builder.ConfigureOpenTelemetry();
    builder.AddDefaultHealthChecks();
    builder.Services.AddServiceDiscovery();
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler();
        http.AddServiceDiscovery();
    });
    return builder;
}

public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
    where TBuilder : IHostApplicationBuilder
{
    builder.Logging.AddOpenTelemetry(o => { o.IncludeFormattedMessage = true; o.IncludeScopes = true; });

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r
            .AddService(builder.Environment.ApplicationName, serviceVersion: BuildInfo.Version)
            .AddAttributes([new("deployment.environment", builder.Environment.EnvironmentName.ToLowerInvariant())]))
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddNpgsql()
            .AddMeter(Telemetry.MeterName))
        .WithTracing(t => t
            .AddSource(Telemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation(o =>
                o.Filter = ctx => ctx.Request.Path != "/alive" && ctx.Request.Path != "/ready")
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true));

    if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        builder.Services.AddOpenTelemetry().UseOtlpExporter();

    return builder;
}
```

Pod-log tailing stays **off** for these services — Alloy would duplicate the OTLP log stream
(helios convention).

---

## 2. Domain instrumentation

There are **eight** domain instruments, declared once, in
`JobHunter.Application/Common/Telemetry.cs`: `RunDuration`, `RunCost`, `JobsDiscovered`,
`JobsDeduplicated`, `BatchLatency`, `DigestCards`, `SourceFailures`, `ParseFailures`. Every other
site (F0 T11) references this count and states **8**.

```csharp
public static class Telemetry
{
    public const string ActivitySourceName = "JobHunter.Pipeline";
    public const string MeterName          = "JobHunter";

    public static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>("jobhunter.run.duration", "s", "End-to-end Run wall clock");
    public static readonly Histogram<double> RunCost =
        Meter.CreateHistogram<double>("jobhunter.run.cost_usd", "USD", "Total LLM spend per Run");
    public static readonly Counter<long> JobsDiscovered =
        Meter.CreateCounter<long>("jobhunter.jobs.discovered", "jobs", "Canonical Jobs after dedup");
    public static readonly Counter<long> JobsDeduplicated =
        Meter.CreateCounter<long>("jobhunter.jobs.deduplicated", "jobs", "Postings merged into an existing Job");
    public static readonly Histogram<double> BatchLatency =
        Meter.CreateHistogram<double>("jobhunter.batch.latency", "s", "Submit → results retrieved");
    public static readonly Counter<long> DigestCards =
        Meter.CreateCounter<long>("jobhunter.digest.cards", "cards", "Cards delivered");
    public static readonly Counter<long> SourceFailures =
        Meter.CreateCounter<long>("jobhunter.source.failures", "failures", "Fetch failures by ats_kind and reason");
    public static readonly Counter<long> ParseFailures =
        Meter.CreateCounter<long>("jobhunter.llm.parse_failures", "items", "LLM items that failed schema validation");
}
```

**Label discipline.** Allowed labels: `stage`, `ats_kind`, `tier`, `environment`, `outcome`.
Forbidden as labels: `job_id`, `company_id`, `run_id` — unbounded cardinality would exhaust the
Grafana Cloud free tier in days. Those go on **spans** as attributes, where cardinality is free.

Every stage handler opens one span:

```csharp
using var activity = Telemetry.Source.StartActivity("stage.enrichment", ActivityKind.Consumer);
activity?.SetTag("run.id", runId);
activity?.SetTag("jobs.count", jobs.Count);
using var scope = logger.BeginScope(new Dictionary<string, object> { ["run_id"] = runId });
```

---

## 3. Health endpoints

| Endpoint | Checks | Auth | Consumer |
|---|---|---|---|
| `/alive` | process is up, nothing else | anonymous | kubelet liveness |
| `/ready` | PostgreSQL, RabbitMQ, Redis reachable | anonymous | kubelet readiness |
| `/health` | the above plus Typesense, Anthropic reachability, last-Run age | `jobhunter:admin` | operator |

`/ready` must **not** check Anthropic or Typesense. A provider outage must not remove a pod from
service — the pipeline is designed to degrade, and a readiness failure would take away the very
component that handles the degradation.

---

## 4. Alerts

| Alert | Condition | Severity | First response |
|---|---|---|---|
| Digest not delivered | no `DigestDelivered` by 07:15 Europe/Kyiv | **page** | [[../operations/runbooks\|R1]] |
| Run stuck | `runs.state` non-terminal for > 6 h | **page** | R2 |
| Cost approaching ceiling | `jobhunter.run.cost_usd` > 70% of ceiling | warn | R3 |
| Cost aborted | any `RunCostAborted` | **page** | R3 |
| Discovery starved | `jobhunter.jobs.discovered` = 0 over 24 h | **page** | R4 |
| Source failure rate | `jobhunter.source.failures` > 20% of attempts over 6 h | warn | R4 |
| Parse failure rate | `jobhunter.llm.parse_failures` > 5% of a batch | warn | R5 |
| Outbox backlog | `wolverine.outgoing.backlog` > 100 for 15 min | warn | R6 |
| Dead-letter growth | any stage DLQ depth > 0 | warn | R6 |
| Pod restart loop | > 3 restarts in 15 min | **page** | R7 |
| Index drift | Typesense vs PostgreSQL count differs > 5% | info | R8 |

"Page" means a Telegram message to the Owner — the same channel as the product, because it is the
one that is actually read.

---

## 5. Dashboards

**Pipeline health** (the one that gets looked at daily)
Run state timeline · jobs discovered vs deduplicated per cycle · per-stage duration · batch latency ·
cost per Run against the ceiling · cards delivered · source success rate by `ats_kind`.

**Cost** — spend per Run, per Stage, per Tier; rolling 30-day total; tokens per job; projection
against the monthly cap.

**Quality** — `precision@10` trend, ignore rate, save rate, suppression count and reasons, parse
failure rate by prompt version. This is the dashboard that says whether the product works, as
opposed to whether the system runs.

**Infrastructure** — the standard ASP.NET Core dashboards from `overflow/docs/grafana/*.json`,
imported with the service names changed.

---

## 6. Log discipline

| Level | Meaning | Example |
|---|---|---|
| `Error` | a human must look | Run failed, cost aborted, source quarantined |
| `Warning` | degraded but self-healing | item parse failure, fetch retry, rejected chat id |
| `Information` | pipeline milestones | Run started/finished, batch submitted, digest delivered |
| `Debug` | per-item detail | one posting normalised, one score computed |

Production runs at `Information`; `Debug` is enabled per-namespace via configuration for an
investigation and turned back off.

**Never logged** (gate G6, [[../CONTEXT]] invariant 12): CV text, prompt bodies, model responses,
API keys, connection strings, Telegram tokens, full job descriptions. A log-scrubbing processor
redacts anything matching the known secret patterns as a second line of defence.

---

## Related

- [[../00-overview/sad]] §7 · [[../operations/runbooks]] · [[../operations/infrastructure]]
- [[../00-overview/adr/0012-otlp-alloy-grafana-cloud|ADR-0012]]
