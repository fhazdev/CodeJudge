variable "subscription_id" {
  description = "Target subscription. Not a secret."
  type        = string
  default     = "6f84ecda-a4e6-4e50-b02d-518f1d498816"
}

variable "location" {
  description = "Azure region. Container Apps consumption and Static Web Apps free tier must both be available here."
  type        = string
  default     = "eastus2"
}

variable "name_prefix" {
  type    = string
  default = "codejudge"
}

variable "environment" {
  description = "Short environment discriminator, used in resource names."
  type        = string
  default     = "prod"
}

variable "api_image" {
  description = <<-EOT
    Container image for the API.

    Defaults to a public Microsoft sample rather than the real image, because Container
    Apps requires a resolvable image at creation time and the GHCR image does not exist
    until CI has pushed one. The first cd-deploy run replaces this; Terraform then ignores
    subsequent changes to it (see the lifecycle block on the container app), so deploys
    and infrastructure do not fight over the same field.
  EOT
  type        = string
  default     = "mcr.microsoft.com/dotnet/samples:aspnetapp"
}

variable "neon_connection_string" {
  description = <<-EOT
    Npgsql connection string for the Neon database.

    Empty means the Key Vault secret is not created, which is the phase 1 state: the
    project is still on docker compose Postgres and no Neon project exists yet.
  EOT
  type        = string
  default     = ""
  sensitive   = true
}

variable "spa_allowed_origin" {
  description = "Origin the API accepts CORS requests from. Set to the Static Web App hostname after the first apply."
  type        = string
  default     = ""
}

variable "entra_api_client_id" {
  description = "Client id of the API app registration, from the identity module's api_client_id output."
  type        = string
  default     = "817d2b95-0ccf-4245-a431-6141e8370be7"
}

variable "log_retention_days" {
  description = "Log Analytics retention. 30 is the free-tier floor and the free grant covers demo volume."
  type        = number
  default     = 30
}
