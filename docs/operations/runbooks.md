---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "06-07"
ticket: ""
tags: [operations, runbooks, jobhunter]
---

# Runbooks

> One runbook per alert in [[../engineering/observability]] §4. Each answers: what happened, how bad
> is it, what do I check, what do I do.
>
> Standing context: `NS=apps-production`, the Owner is the only operator, and almost nothing here is
> urgent enough to act on before coffee — the worst realistic outcome is one missed digest.

---

## R1 — Digest not delivered by 07:15

**Severity:** page · **Impact:** the product did not happen today

```bash
# 1. Where did the Run stop?
kubectl exec -n infra-production $PGPOD -- psql -U postgres -d production_jobhunter -c \
  "SELECT id, state, started_at, finished_at, spent_usd, failure_reason
     FROM runs ORDER BY started_at DESC LIMIT 3;"

# 2. Did the digest get generated but not sent?
kubectl logs -n $NS deploy/jobhunter-telegram --tail=100 | grep -Ei 'digest|deliver|error'

# 3. Is the bot alive at all?
kubectl get pods -n $NS -l app=jobhunter-telegram
```

| `runs.state` | Meaning | Action |
|---|---|---|
| `Delivered` | Digest sent; Telegram or the network dropped it | Re-deliver: `POST /api/runs/{id}/redeliver`. `delivery_log` prevents duplicates (invariant 8) |
| `Enriching` / `Matching` | A batch is still in flight | Normal if within the 24 h SLA. Check `batches.state`; if `Submitted` for > 6 h, go to R2 |
| `CostAborted` | Ceiling hit | Go to R3 |
| `Failed` | Read `failure_reason` | Fix the cause, then `POST /api/runs/{id}/resume` |
| No row for today | The schedule did not fire | Check Hangfire: `kubectl port-forward -n $NS deploy/jobhunter-worker 8080:8080` → `/hangfire` |

**Recovery is always safe.** Resuming a Run re-enters the state machine at its current state; it
never resubmits a completed Batch and never re-delivers a logged Card.

---

## R2 — Run stuck (non-terminal for > 6 h)

**Severity:** page · **Impact:** today's digest is at risk

```bash
kubectl exec -n infra-production $PGPOD -- psql -U postgres -d production_jobhunter -c \
  "SELECT b.id, b.stage, b.tier, b.state, b.provider_batch_id, b.poll_attempts, b.submitted_at
     FROM batches b JOIN runs r ON r.id = b.run_id
    WHERE r.state NOT IN ('Delivered','Failed','CostAborted');"
```

1. **`Submitted`, `poll_attempts` not increasing** → the poller is not running. Restart the worker;
   it re-reads `provider_batch_id` from the database and resumes polling. It will not resubmit.
   ```bash
   kubectl rollout restart deployment/jobhunter-worker -n $NS
   ```
2. **`poll_attempts` increasing, provider still `in_progress`** → Anthropic is slow. Wait. If 07:00
   arrives first, the partial-digest policy handles it automatically.
3. **Provider reports the batch as expired or cancelled** → mark the batch failed and let the stage
   re-submit on the next Run:
   ```sql
   UPDATE batches SET state = 'Failed' WHERE id = '<batch-id>';
   ```
4. **The worker is CrashLooping** → R7.

---

## R3 — Cost ceiling approached or breached

**Severity:** warn at 70%, page on abort · **Impact:** reduced or missing digest

```sql
SELECT stage, tier, SUM(cost_usd) AS usd, SUM(input_tokens + output_tokens) AS tokens
  FROM cost_ledger_entries WHERE run_id = '<run-id>' GROUP BY stage, tier;
```

Diagnose before raising the ceiling — a breach is almost always a symptom:

| Cause | Signal | Fix |
|---|---|---|
| Job volume spike | `jobs_in_scope` far above the trailing average | Legitimate. Raise `Run:CeilingUsd` for the day |
| Dedup regression | `jobhunter.jobs.deduplicated` collapsed | Real bug — go to R4 and fix dedup, do not raise the ceiling |
| Retry storm | `poll_attempts` very high, or repeated batch submissions | Check the DLQ (R6). A resubmission loop is a defect |
| Prompt bloat | Tokens per job up sharply after a deploy | Compare `prompt_version`; revert or trim the prompt |

Raising the ceiling is a config change in Infisical plus a worker restart. Resume the Run afterwards;
already-completed stages are not recharged.

---

## R4 — Discovery starved or source failures

**Severity:** page at zero jobs / warn above 20% failures · **Impact:** inventory stops, quietly

```bash
kubectl exec -n infra-production $PGPOD -- psql -U postgres -d production_jobhunter -c \
  "SELECT s.ats_kind, count(*) FILTER (WHERE l.http_status BETWEEN 200 AND 299) AS ok,
          count(*) FILTER (WHERE l.http_status >= 400) AS failed
     FROM source_fetch_log l JOIN job_sources s ON s.id = l.source_id
    WHERE l.started_at > now() - interval '6 hours'
    GROUP BY s.ats_kind;"
```

| Pattern | Likely cause | Action |
|---|---|---|
| One `ats_kind` at 100% failure | The provider changed its API shape | Run the contract test suite for that adapter; fix the adapter. Other sources are unaffected |
| Many hosts returning 429 | Rate budget too aggressive, or a shared egress IP | Lower `Discovery:RequestsPerSecondPerHost`; verify the Redis token bucket is actually being consulted |
| All sources failing | Cluster egress or DNS | `kubectl exec deploy/jobhunter-worker -- curl -sS -o /dev/null -w '%{http_code}' https://boards-api.greenhouse.io/v1/boards/stripe/jobs` |
| Zero jobs but no failures | Empty or fully quarantined registry | `SELECT count(*) FROM companies WHERE is_active;` — then unquarantine or re-seed |

Un-quarantine a source once fixed — through the admin endpoint (F9-T07), so recovery needs no database
access. It answers `200 {"outcome":"Released"}` when the hold was lifted, `200 {"outcome":"NotQuarantined"}`
when the source was already healthy, and `404` for an unknown id:
```bash
curl -X POST https://jobhunter.devoverflow.org/api/admin/sources/<source-id>/unquarantine \
  -H "Authorization: Bearer $TOKEN"
```
The endpoint requires the `jobhunter:admin` scope. Prefer it to a direct `UPDATE job_sources` — the
aggregate also resets the consecutive-failure counter so the next cycle starts clean.

---

## R5 — LLM parse failures above 5%

**Severity:** warn · **Impact:** degraded digest quality; the Run still completes

```sql
SELECT b.stage, b.prompt_version, count(*) FILTER (WHERE bi.state = 'ParseFailed') AS failed, count(*) AS total
  FROM batch_items bi JOIN batches b ON b.id = bi.batch_id
 WHERE b.run_id = '<run-id>' GROUP BY b.stage, b.prompt_version;

SELECT bi.parse_error, bi.raw_result FROM batch_items bi
 WHERE bi.state = 'ParseFailed' LIMIT 5;
```

- **Failures started after a prompt change** → the schema and the prompt disagree. Revert the
  `PromptVersion`, reproduce against the saved fixture, fix, and add the failing payload as a new
  fixture before redeploying.
- **Failures with an unchanged prompt version** → provider-side drift. The nightly drift job should
  already have alerted. Capture the payloads as fixtures and loosen the parser to degrade rather
  than reject.
- **A single recurring shape** (a truncated description, an unusual currency) → a parser gap, not a
  model problem. Fix the parser.

Failed items retry once at cheap tier in the next Run automatically. No manual replay is needed.

---

## R6 — Outbox backlog or dead-letter growth

**Severity:** warn · **Impact:** the pipeline stalls silently between stages

```bash
kubectl exec -n infra-production $PGPOD -- psql -U postgres -d production_jobhunter -c \
  "SELECT message_type, count(*) FROM wolverine_outgoing_envelopes GROUP BY message_type;"

kubectl exec -n infra-production $RMQ -- rabbitmqctl list_queues -p jobhunter-production name messages | grep -i dlq
```

- **Outbox growing, DLQ empty** → RabbitMQ is unreachable from the worker. Check the vhost, the
  credentials and the pod's network. The outbox is doing its job — nothing is lost, it will drain.
- **DLQ growing** → a poison message. Inspect it, fix the handler, then replay:
  ```bash
  kubectl exec deploy/jobhunter-worker -n $NS -- dotnet JobHunter.Worker.dll replay-dlq --stage enrichment --max 50
  ```
- **Both growing** → the worker is unhealthy. R7.

Never purge a DLQ without reading a message first. The message is the bug report.

---

## R7 — Pod restart loop

**Severity:** page

```bash
kubectl get pods -n $NS
kubectl describe pod -n $NS <pod>
kubectl logs -n $NS <pod> --previous --tail=80
```

| Symptom in logs | Cause | Fix |
|---|---|---|
| `Infisical login failed` / `no secrets returned` | Machine identity revoked or expired | Rotate in Infisical, update the GitHub secret, redeploy |
| `Npgsql… password authentication failed` | Password rotated without updating Infisical | Update Infisical, restart |
| `relation "…" does not exist` | The migrator Job did not run | `kubectl get job -n $NS jobhunter-migrator-<sha>`; rerun it, then restart the deployment |
| Startup probe timeout | Cold start slower than 150 s | Raise `startupProbe.failureThreshold`; check whether Infisical is slow |
| `OOMKilled` | Memory limit hit | Usually a discovery fan-out unbounded by parallelism. Raise the limit *and* find the unbounded loop |

---

## R8 — Typesense index drift

**Severity:** info · **Impact:** search results stale; the digest is unaffected

Check the drift through the admin stats endpoint (F9-T07) — no Typesense or database access needed. It
returns the authoritative live-job count, the index document count and the normalised drift between them
(the same figure the nightly reconcile acts on), and stays answerable with `"indexAvailable": false`
even while Typesense is down:
```bash
curl -s https://jobhunter.devoverflow.org/api/admin/stats -H "Authorization: Bearer $TOKEN" | jq .
```

Full rebuild — safe at any time, takes minutes, and the digest never reads from Typesense. The endpoint
enqueues the rebuild and answers `202` with the operation id rather than blocking:
```bash
curl -X POST https://jobhunter.devoverflow.org/api/admin/search/reindex -H "Authorization: Bearer $TOKEN"
```

If normalisation itself was wrong (a bad extraction rule, since fixed), re-normalise the affected jobs
from their immutable raw postings through the reprocess endpoint (F2 AC-09) — `firstSeenFrom` bounds the
window, and an absent body reprocesses the full history:
```bash
curl -X POST https://jobhunter.devoverflow.org/api/admin/jobs/reprocess \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"firstSeenFrom":"2026-01-01T00:00:00Z"}'
```

All three endpoints require the `jobhunter:admin` scope.

---

## R9 — Restore PostgreSQL from backup

**Severity:** disaster · **RTO ~30 min**

This restores from the nightly `pg_dump` → Azure Blob backup produced by the backup job (**F0 T15**);
without that job there is nothing to restore, which is why it is a hard prerequisite of this runbook.

```bash
az storage blob download --account-name stheliosinfrastate --container-name jobhunter-backups \
  --name "production_jobhunter_$(date -d yesterday +%Y%m%d).sql.gz" --file restore.sql.gz
gunzip restore.sql.gz

# ALWAYS restore into a scratch database first and verify, never straight over production
kubectl exec -i -n infra-production $PGPOD -- psql -U postgres -c "CREATE DATABASE restore_check;"
kubectl exec -i -n infra-production $PGPOD -- psql -U postgres -d restore_check < restore.sql
kubectl exec -n infra-production $PGPOD -- psql -U postgres -d restore_check -c \
  "SELECT count(*) FROM jobs; SELECT max(started_at) FROM runs;"
```

Only after those counts look right, scale the deployments to zero, swap the databases, and scale
back up. Then re-index Typesense (R8).

**This procedure is untested until it has been rehearsed.** Tracked as a blocker in [[../BACKLOG]] §5.

---

## R10 — Rotate a credential

| Credential | Steps |
|---|---|
| Anthropic API key | New key in the console → update `/app/services/ANTHROPIC_API_KEY` in Infisical → `kubectl rollout restart deployment/jobhunter-worker` → revoke the old key |
| Telegram bot token | `/revoke` then `/token` with BotFather → update Infisical → restart `jobhunter-telegram` |
| PostgreSQL / RabbitMQ / Redis passwords | Rotate in `infrastructure-helios` → update Infisical → restart all three deployments |
| Keycloak client secret | Regenerate in Keycloak → update Infisical → restart `jobhunter-api` |
| Infisical machine identity | New identity → update the three GitHub secrets → redeploy → revoke the old identity |

Rotation order is always: **create new → distribute → restart → verify → revoke old.** Revoking
first turns a routine rotation into an outage.

---

## Quick reference

```bash
export NS=apps-production
export PGPOD=$(kubectl get pod -n infra-production -l app.kubernetes.io/name=postgresql -o jsonpath='{.items[0].metadata.name}')
export RMQ=$(kubectl get pod -n infra-production -l app.kubernetes.io/name=rabbitmq -o jsonpath='{.items[0].metadata.name}')

kubectl get pods -n $NS -l app.kubernetes.io/part-of=jobhunter
kubectl logs -n $NS deploy/jobhunter-worker -f --tail=50
kubectl port-forward -n $NS deploy/jobhunter-worker 8080:8080     # then http://localhost:8080/hangfire
kubectl exec -n infra-production $PGPOD -- psql -U postgres -d production_jobhunter
kubectl exec -n infra-production $RMQ  -- rabbitmqctl list_queues -p jobhunter-production
```

---

## Related

- [[../engineering/observability]] §4 · [[../engineering/deployment]] · [[infrastructure]]
