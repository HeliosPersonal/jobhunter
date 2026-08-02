---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "06-07"
ticket: ""
tags: [engineering, deployment, jobhunter]
---

# Deployment

> Three deployables on the helios k3s cluster, deployed by Kustomize overlays.
> Cluster facts and shared-service endpoints: [[../operations/infrastructure]].

---

## 1. Layout

```text
k8s/
├── base/
│   ├── api/{deployment.yaml, service.yaml, kustomization.yaml}
│   ├── worker/{deployment.yaml, kustomization.yaml}          # no Service — no inbound traffic
│   ├── telegram/{deployment.yaml, kustomization.yaml}        # no Service — long-poll, outbound only
│   ├── migrator/{job.yaml, kustomization.yaml}               # EF migrations as a pre-deploy Job
│   └── infisical/{secret.yaml, kustomization.yaml}           # placeholders only, substituted by CI
├── overlays/
│   ├── staging/{kustomization.yaml, ingress.yaml}
│   └── production/{kustomization.yaml, ingress.yaml}
└── scripts/{reset-staging.sh, cleanup-resources.sh}
```

---

## 2. Deployment manifest

`k8s/base/worker/deployment.yaml` — the most constrained of the three:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: jobhunter-worker
  labels: { app: jobhunter-worker }
spec:
  replicas: 1                 # MUST stay 1 — Hangfire schedules + Run orchestration are singleton
  strategy:
    type: Recreate            # never two orchestrators alive at once, not even for a second
  selector:
    matchLabels: { app: jobhunter-worker }
  template:
    metadata:
      labels: { app: jobhunter-worker }
    spec:
      imagePullSecrets: [{ name: ghcr-pull-secret }]
      containers:
        - name: worker
          image: jobhunter-worker            # remapped by the overlay's images: block
          envFrom:
            - configMapRef: { name: jobhunter-infra-config }   # Terraform-managed
          env:
            - name: ASPNETCORE_ENVIRONMENT
              valueFrom: { configMapKeyRef: { name: app-config, key: aspnetcore-environment } }
            - name: ASPNETCORE_HTTP_PORTS
              value: "8080"
            - name: OTEL_SERVICE_NAME
              value: "jobhunter-worker"
            - name: INFISICAL_CLIENT_ID
              valueFrom: { secretKeyRef: { name: infisical-credentials, key: INFISICAL_CLIENT_ID } }
            - name: INFISICAL_CLIENT_SECRET
              valueFrom: { secretKeyRef: { name: infisical-credentials, key: INFISICAL_CLIENT_SECRET } }
            - name: INFISICAL_PROJECT_ID
              valueFrom: { secretKeyRef: { name: infisical-credentials, key: INFISICAL_PROJECT_ID } }
          livenessProbe:
            httpGet:  { path: /alive, port: 8080 }
            initialDelaySeconds: 20
            periodSeconds: 30
          readinessProbe:
            httpGet:  { path: /ready, port: 8080 }
            initialDelaySeconds: 10
            periodSeconds: 15
          startupProbe:
            httpGet:  { path: /alive, port: 8080 }
            failureThreshold: 30
            periodSeconds: 5          # Infisical fetch + migration check can be slow on cold start
          resources:
            requests: { memory: 256Mi, cpu: 100m }
            limits:   { memory: 1Gi }           # no CPU limit — batch polling is bursty
```

`replicas: 1` and `strategy: Recreate` are load-bearing, not defaults. They are the manifest
expression of [[../00-overview/sad]] §11 D2.

---

## 3. Overlay

`k8s/overlays/production/kustomization.yaml`:

```yaml
apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization
namespace: apps-production

resources:
  - ../../base/api
  - ../../base/worker
  - ../../base/telegram
  - ../../base/migrator
  - ../../base/infisical
  - ingress.yaml

images:
  - name: jobhunter-api
    newName: ghcr.io/GITHUB_USERNAME/jobhunter-api
    newTag: SHA_REPLACED_BY_CICD
  - name: jobhunter-worker
    newName: ghcr.io/GITHUB_USERNAME/jobhunter-worker
    newTag: SHA_REPLACED_BY_CICD
  - name: jobhunter-telegram
    newName: ghcr.io/GITHUB_USERNAME/jobhunter-telegram
    newTag: SHA_REPLACED_BY_CICD

configMapGenerator:
  - name: app-config
    literals: [aspnetcore-environment=Production]

replicas:
  - { name: jobhunter-api, count: 2 }
  - { name: jobhunter-worker, count: 1 }
  - { name: jobhunter-telegram, count: 1 }

labels:
  - pairs: { app.kubernetes.io/part-of: jobhunter, environment: production }
```

Both `GITHUB_USERNAME` and `SHA_REPLACED_BY_CICD` are substituted by `sed` in CI
([[ci-cd]] §2). A `kubectl kustomize` preview runs before every apply so an unsubstituted
placeholder is visible in the log rather than at pod-pull time.

---

## 4. Terraform-managed configuration

`terraform/` consumes the helios remote state and creates only what is JobHunter-specific:

```hcl
terraform {
  required_version = ">= 1.5"
  backend "azurerm" {
    resource_group_name  = "rg-helios-tfstate"
    storage_account_name = "stheliosinfrastate"
    container_name       = "tfstate"
    key                  = "jobhunter.tfstate"
    use_azuread_auth     = true
  }
}

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
  o             = data.terraform_remote_state.infra.outputs
  environments  = { staging = local.o.namespace_apps_staging, production = local.o.namespace_apps_production }
}

# Databases — created by exec into the shared Postgres pod, idempotently
resource "null_resource" "databases" {
  for_each = local.environments
  triggers = { db = "${each.key}_jobhunter" }
  provisioner "local-exec" {
    command = <<-EOT
      POD=$(kubectl get pod -n infra-production -l app.kubernetes.io/name=postgresql -o jsonpath='{.items[0].metadata.name}')
      kubectl exec -n infra-production "$POD" -- psql -U postgres -tc \
        "SELECT 1 FROM pg_database WHERE datname='${each.key}_jobhunter'" | grep -q 1 || \
      kubectl exec -n infra-production "$POD" -- psql -U postgres -c \
        "CREATE DATABASE ${each.key}_jobhunter;"
    EOT
  }
}

resource "kubernetes_config_map_v1" "jobhunter_infra_config" {
  for_each = local.environments
  metadata { name = "jobhunter-infra-config"  namespace = each.value }
  data = {
    ConnectionStrings__JobHunter = "Host=${local.o.postgres_host};Port=${local.o.postgres_port};Database=${each.key}_jobhunter;Username=postgres"
    ConnectionStrings__Messaging = "amqp://admin@${local.o.rabbitmq_host}:${local.o.rabbitmq_amqp_port}/jobhunter-${each.key}"
    ConnectionStrings__Cache     = "${local.o.redis_host}:${local.o.redis_port}"
    Redis__KeyPrefix             = "${each.key}:jobhunter:"
    Typesense__Url               = local.o.typesense_url
    Typesense__CollectionPrefix  = "${each.key}_jobhunter_"
    Keycloak__Authority          = "${local.o.keycloak_external_url}/realms/jobhunter"
    OTEL_EXPORTER_OTLP_ENDPOINT  = local.o.otlp_http_endpoint
    OTEL_EXPORTER_OTLP_PROTOCOL  = "http/protobuf"
  }
}
```

**Passwords are not in this ConfigMap.** They arrive from Infisical at startup and are appended to
the connection strings by `AddEnvVariablesAndConfigureSecrets()`.

The RabbitMQ vhosts (`jobhunter-staging`, `jobhunter-production`) are created once by hand — the
same runbook `overflow` uses:

```bash
POD=$(kubectl get pod -n infra-production -l app.kubernetes.io/name=rabbitmq -o jsonpath='{.items[0].metadata.name}')
for V in jobhunter-staging jobhunter-production; do
  kubectl exec -n infra-production "$POD" -- rabbitmqctl add_vhost "$V"
  kubectl exec -n infra-production "$POD" -- rabbitmqctl set_permissions -p "$V" admin ".*" ".*" ".*"
done
```

---

## 5. Migrations

Migrations run as a Kubernetes `Job` before the Deployments roll, **never** by the application at
startup — three pods racing `Database.Migrate()` is a deadlock waiting for a bad night.

```yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: jobhunter-migrator
  annotations: { argocd.argoproj.io/hook: PreSync }
spec:
  backoffLimit: 2
  template:
    spec:
      restartPolicy: Never
      containers:
        - name: migrator
          image: jobhunter-worker
          args: ["migrate"]          # the Worker's CLI entry point, exits 0 on success
          envFrom: [{ configMapRef: { name: jobhunter-infra-config } }]
```

The Job name includes the commit SHA in the overlay so each deploy creates a fresh Job rather than
colliding with an immutable completed one.

---

## 6. Ingress

Only the API is exposed. TLS terminates at Cloudflare; the origin certificate is the shared
`cloudflare-origin` secret copied into the namespace by Terraform.

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: jobhunter-api
  annotations:
    nginx.ingress.kubernetes.io/rewrite-target: /$2
spec:
  ingressClassName: nginx
  tls:
    - hosts: [jobhunter.devoverflow.org]
      secretName: cloudflare-origin
  rules:
    - host: jobhunter.devoverflow.org
      http:
        paths:
          - path: /api(/|$)(.*)
            pathType: ImplementationSpecific
            backend: { service: { name: jobhunter-api, port: { number: 8080 } } }
```

The Hangfire dashboard is **not** in the ingress. It is reached by port-forward and additionally
requires the `jobhunter:admin` scope ([[../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]]).

---

## 7. Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/JobHunter.Worker/*.csproj          src/JobHunter.Worker/
COPY src/JobHunter.Application/*.csproj     src/JobHunter.Application/
COPY src/JobHunter.Domain/*.csproj          src/JobHunter.Domain/
COPY src/JobHunter.Infrastructure/*.csproj  src/JobHunter.Infrastructure/
COPY src/JobHunter.Contracts/*.csproj       src/JobHunter.Contracts/
COPY src/JobHunter.Claude/*.csproj          src/JobHunter.Claude/
COPY src/JobHunter.Scrapers/*.csproj        src/JobHunter.Scrapers/
COPY src/JobHunter.Search/*.csproj          src/JobHunter.Search/
COPY src/Aspire/JobHunter.ServiceDefaults/*.csproj src/Aspire/JobHunter.ServiceDefaults/
RUN dotnet restore src/JobHunter.Worker/JobHunter.Worker.csproj
COPY . .
RUN dotnet publish src/JobHunter.Worker/JobHunter.Worker.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobHunter.Worker.dll"]
```

`.csproj` files are copied before the source so `dotnet restore` is cached across source-only
changes. `USER $APP_UID` is explicit rather than relying on the base image default.

---

## Related

- [[ci-cd]] · [[../operations/infrastructure]] · [[../operations/runbooks]]
- [[../00-overview/adr/0010-kustomize-ghcr-selfhosted-runner|ADR-0010]]
