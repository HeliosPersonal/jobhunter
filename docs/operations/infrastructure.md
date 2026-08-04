---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "06-07"
ticket: ""
tags: [operations, infrastructure, jobhunter]
---

# Infrastructure — JobHunter on helios

> JobHunter does not provision infrastructure. It **consumes** the shared `helios` k3s cluster
> managed by the `infrastructure-helios` repository, and adds only its own database, vhost,
> key prefix, collection prefix and ConfigMap.

---

## 1. The cluster

| | |
|---|---|
| Cluster | `helios` (k3s, single node) |
| Node | `10.12.15.60` |
| Public domain | `devoverflow.org` |
| Internal domain | `*.helios` |
| Terraform state | Azure Blob — `stheliosinfrastate/tfstate/infrastructure-helios.tfstate` |
| Traffic path | Internet → Cloudflare (DNS/WAF/TLS) → `cloudflared` tunnel → NGINX ingress → service |

**Namespaces:** `infra-production` (shared services) · `apps-staging` · `apps-production` ·
`ingress` · `monitoring`

**Shared services** in `infra-production`, single instance each, multi-tenant by naming convention:
PostgreSQL · RabbitMQ · Redis · Typesense · Keycloak · Ollama · Redis Insight.

---

## 2. What JobHunter uses

| Service | JobHunter's slice | Purpose |
|---|---|---|
| PostgreSQL | database `{env}_jobhunter` | System of record + Hangfire (`hangfire` schema) |
| RabbitMQ | vhost `jobhunter-{env}` | Stage-to-stage transport |
| Redis | key prefix `{env}:jobhunter:` | Rate-limit buckets, dedup filter, response cache |
| Typesense | collections `{env}_jobhunter_*` | Job search index |
| Keycloak | realm `jobhunter` | API authentication |
| Ollama | shared instance | Cheap-tier fallback and offline development |
| Grafana Alloy | OTLP endpoint | Telemetry egress to Grafana Cloud |
| NGINX ingress | `jobhunter{,-staging}.devoverflow.org` | API exposure |

These names follow the helios convention exactly (`{env}_{app}`, `{env}-{app}`, `{env}:{app}:`,
`{env}_{app}_{name}`) so JobHunter is a well-behaved tenant of shared instances.

---

## 3. Connection reference

Values come from the helios Terraform outputs via remote state; passwords come from Infisical.

```text
PostgreSQL   postgres.infra-production.svc.cluster.local:5432
             database: production_jobhunter | staging_jobhunter
             Host=postgres.infra-production.svc.cluster.local;Port=5432;Database=production_jobhunter;Username=postgres;Password=<infisical>

RabbitMQ     rabbitmq.infra-production.svc.cluster.local:5672   (mgmt 15672)
             vhost: jobhunter-production | jobhunter-staging
             amqp://admin:<infisical>@rabbitmq.infra-production.svc.cluster.local:5672/jobhunter-production

Redis        redis.infra-production.svc.cluster.local:6379, db 0 always
             key prefix: production:jobhunter:  (never use DB numbers for isolation)

Typesense    http://typesense.infra-production.svc.cluster.local:8108
             collections: production_jobhunter_jobs

Keycloak     internal  http://keycloak.infra-production.svc.cluster.local:8080
             external  https://keycloak.devoverflow.org/realms/jobhunter

Ollama       http://ollama.infra-production.svc.cluster.local:11434

OTLP         http://grafana-alloy.monitoring.svc.cluster.local:4318   (gRPC :4317)
```

---

## 4. Consuming the shared state

`terraform/data.tf`:

```hcl
data "terraform_remote_state" "infra" {
  backend = "azurerm"
  config = {
    resource_group_name  = "rg-helios-tfstate"
    storage_account_name = "stheliosinfrastate"
    container_name       = "tfstate"
    key                  = "infrastructure-helios.tfstate"
    use_azuread_auth     = true
  }
}

locals {
  o = data.terraform_remote_state.infra.outputs

  postgres_host     = local.o.postgres_host
  rabbitmq_host     = local.o.rabbitmq_host
  redis_host        = local.o.redis_host
  typesense_url     = local.o.typesense_url
  keycloak_url      = local.o.keycloak_external_url
  otlp_endpoint     = local.o.otlp_http_endpoint
  base_domain       = local.o.base_domain
  ns_staging        = local.o.namespace_apps_staging
  ns_production     = local.o.namespace_apps_production
}
```

JobHunter's own state is `stheliosinfrastate/tfstate/jobhunter.tfstate`, a separate key so a
JobHunter apply can never damage the shared infrastructure state.

---

## 5. One-time provisioning

Run once, in order, before the first deployment.

```bash
# 1. Databases (idempotent — Terraform also does this, this is the manual path)
POD=$(kubectl get pod -n infra-production -l app.kubernetes.io/name=postgresql -o jsonpath='{.items[0].metadata.name}')
kubectl exec -n infra-production "$POD" -- psql -U postgres -c "CREATE DATABASE staging_jobhunter;"
kubectl exec -n infra-production "$POD" -- psql -U postgres -c "CREATE DATABASE production_jobhunter;"

# 2. RabbitMQ vhosts (not Terraform-managed, by convention)
RMQ=$(kubectl get pod -n infra-production -l app.kubernetes.io/name=rabbitmq -o jsonpath='{.items[0].metadata.name}')
for V in jobhunter-staging jobhunter-production; do
  kubectl exec -n infra-production "$RMQ" -- rabbitmqctl add_vhost "$V"
  kubectl exec -n infra-production "$RMQ" -- rabbitmqctl set_permissions -p "$V" admin ".*" ".*" ".*"
done

# 3. Keycloak realm + clients — import docs/operations/keycloak/jobhunter-realm.json
#    clients: jobhunter-api (bearer-only), jobhunter-cli (client credentials)
#    scopes:  jobhunter:read, jobhunter:admin

# 4. Infisical project "jobhunter", environments staging + production, paths:
#    /app/connections  POSTGRES_PASSWORD, RABBITMQ_PASSWORD, REDIS_PASSWORD
#    /app/auth         KEYCLOAK_CLIENT_SECRET
#    /app/services     ANTHROPIC_API_KEY, TELEGRAM_BOT_TOKEN, TELEGRAM_OWNER_CHAT_ID, TYPESENSE_API_KEY

# 5. Cloudflare DNS — jobhunter.devoverflow.org and jobhunter-staging.devoverflow.org
#    proxied through the existing tunnel; TLS mode Full (strict)
```

---

## 6. Resource footprint

| Deployable | Requests | Limits | Replicas (stg/prod) |
|---|---|---|---|
| `jobhunter-api` | 128Mi / 50m | 512Mi | 1 / 2 |
| `jobhunter-worker` | 256Mi / 100m | 1Gi | 1 / 1 |
| `jobhunter-telegram` | 128Mi / 50m | 512Mi | 1 / 1 |

Total steady-state footprint under 1 GB across both environments. No CPU limits — batch polling and
discovery fan-out are bursty, and a CPU limit would throttle exactly the work that matters.

**Storage:** none of its own. All state is in the shared PostgreSQL; there is no PVC and no local
volume, which is what makes a pod fully disposable.

---

## 7. Backup and recovery

| Asset | Strategy | RPO | RTO |
|---|---|---|---|
| PostgreSQL | Nightly `pg_dump` to Azure Blob, 30-day retention | 24 h | ~30 min |
| Typesense index | Not backed up — rebuilt from PostgreSQL | n/a | ~10 min |
| RawPostings | Backed up with the database; also re-fetchable from source | 24 h | hours |
| Terraform state | Azure Blob versioning, promote a prior version | immediate | minutes |
| Secrets | Infisical (SaaS, vendor-managed) | n/a | n/a |
| Container images | GHCR, tagged by SHA, retained indefinitely | n/a | n/a |

Losing a day of data costs one digest. Every derived artifact (Jobs, Enrichments, index) can be
recomputed from `raw_postings`, and `raw_postings` can be re-fetched. This is why the single-node
risk (R9) is accepted rather than mitigated with hardware.

The nightly `pg_dump` → Azure Blob backup job is a real task (**F0 T15**) — the source R9 restores
from. **It is only fully trusted once the restore has been rehearsed** — the rehearsal is tracked in
[[../BACKLOG]] §5.

---

## 8. Cost

| Item | Monthly |
|---|---|
| helios cluster | £0 marginal — existing home lab |
| Grafana Cloud | £0 — free tier |
| Infisical | £0 — free tier |
| GHCR | £0 — public repository |
| Azure Blob (state + backups) | ~£0.50 (≈ $0.65) |
| Anthropic API | ~$31 at 150 jobs/day (see breakdown below) |
| **Total** | **~$31.50/month** |

The £ line items are converted at **£1 ≈ $1.27** before summing with the $ line items. Only Azure
Blob is billed in £ (~£0.50 ≈ $0.65); the total rounds to ~$31.50/month, dominated by the Anthropic
line.

### Anthropic API breakdown

Verified against list pricing on 2026-08-02. Batch discount (50%) applied throughout
([[../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]]).

| Model | Tier | Input $/1M | Output $/1M |
|---|---|---|---|
| `claude-haiku-4-5` | `Cheap` | 1.00 | 5.00 |
| `claude-sonnet-5` | `Deep` | 3.00 | 15.00 |
| `claude-opus-5` | `Deep` (upgrade path) | 5.00 | 25.00 |

At 150 newly discovered jobs/day:

| Stage | Items | $/day naive | $/day optimised |
|---|---|---|---|
| Enrichment (cheap) | 150 | $0.43 | $0.43 |
| Matching (deep) | 150 → 60 after pre-filter | $1.58 | $0.44 |
| Synthesis (deep) | 1 | $0.01 | $0.01 |
| Research (deep) | 5 | $0.14 | $0.14 |
| **Total** | | **$2.16** | **$1.03** |
| **Per 30-day month** | | **$65** | **$31** |

Two optimisations account for the difference, both owned by
[[../features/f4-cv-matching-ranking/index|F4]] and decided in
[[../features/f4-cv-matching-ranking/adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]]:

1. **Pre-match filter** — enrichment already establishes salary band, remote policy, timezone band
   and contractor friendliness. Most jobs are excluded on those facts alone, with no CV comparison
   needed. Passing ~40% through to the deep tier removes ~60% of the largest line item.
2. **Prompt caching of the CV prefix** — the CV (~2 000 tokens) and system prompt (~400) are
   byte-identical across every item in a matching batch, roughly 47% of input. Cached tokens read at
   0.1× ([[../features/f4-cv-matching-ranking/adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]]).
   The `cache_control` breakpoint is placed at the end of the CV prefix by the Anthropic request
   builder (F4 T13); the batch is priced pessimistically at the *full* input (the cache discount is a
   retrieval-time saving, never assumed by the pre-submission ceiling), so the $0.44 figure is the
   worst case the ceiling gates and the cached run comes in at or under it. The mechanism is asserted
   with zero network — a byte-identical prefix carrying exactly one breakpoint on every item, and a
   parsed `cache_read_input_tokens > 0` on every item after the first. The **empirical** cache-hit
   rate against the live API, and the measured Run cost that confirms the $1.03 above, are verified by
   the opt-in weekly live suite together with the regret sampler (F4 T21).

**Sensitivity.** Cost scales close to linearly with jobs discovered per day: 75/day ≈ $16/month,
300/day ≈ $62/month at the optimised configuration. Raising the deep tier to `claude-opus-5` roughly
doubles the matching line.

> Sonnet 5 carries introductory pricing ($2.00/$10.00) through **2026-08-31**, which reduces the
> figures above by roughly a third until then. Budget against the standard rate.

---

## Related

- [[../engineering/deployment]] · [[../engineering/ci-cd]] · [[runbooks]]
- `infrastructure-helios` repository — `docs/QUICK_REFERENCE.md`, `docs/OBSERVABILITY.md`
