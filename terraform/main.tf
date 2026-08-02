# JobHunter-specific infrastructure only. The shared helios cluster (Postgres, RabbitMQ, Redis,
# Typesense, Keycloak) is managed by the sibling `infrastructure-helios` state, consumed here as a
# remote data source. This file creates the two databases and the per-environment
# jobhunter-infra-config ConfigMap. Passwords are NOT written here — they come from Infisical at
# pod startup (deployment §4, invariant 12).

terraform {
  required_version = ">= 1.5"

  backend "azurerm" {
    resource_group_name  = "rg-helios-tfstate"
    storage_account_name = "stheliosinfrastate"
    container_name       = "tfstate"
    key                  = "jobhunter.tfstate"
    use_azuread_auth     = true
  }

  required_providers {
    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = ">= 2.30"
    }
    null = {
      source  = "hashicorp/null"
      version = ">= 3.2"
    }
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
  o = data.terraform_remote_state.infra.outputs
  environments = {
    staging    = local.o.namespace_apps_staging
    production = local.o.namespace_apps_production
  }
}

# Databases — created by exec into the shared Postgres pod, idempotently (deployment §4).
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

  metadata {
    name      = "jobhunter-infra-config"
    namespace = each.value
  }

  # No passwords here — Infisical appends them at startup via AddEnvVariablesAndConfigureSecrets().
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
