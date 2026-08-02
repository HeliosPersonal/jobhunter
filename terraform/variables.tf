# Shared-infrastructure passwords supplied by CI as TF_VAR_* from GitHub secrets (ci-cd §2). They are
# marked sensitive so Terraform never prints them, and they are never written into the ConfigMap.
variable "pg_password" {
  description = "PostgreSQL superuser password for the shared helios instance."
  type        = string
  sensitive   = true
  default     = ""
}

variable "rabbit_password" {
  description = "RabbitMQ admin password for the shared helios instance."
  type        = string
  sensitive   = true
  default     = ""
}

variable "redis_password" {
  description = "Redis password for the shared helios instance."
  type        = string
  sensitive   = true
  default     = ""
}

variable "typesense_api_key" {
  description = "Typesense admin API key for the shared helios instance."
  type        = string
  sensitive   = true
  default     = ""
}
