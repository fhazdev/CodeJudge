variable "subscription_id" {
  description = "Target subscription. Not a secret."
  type        = string
  default     = "6f84ecda-a4e6-4e50-b02d-518f1d498816"
}

variable "location" {
  description = "Azure region. Container Apps consumption and Static Web Apps free tier must both be available here."
  type        = string
  default     = "centralus"
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

variable "judge_image" {
  description = <<-EOT
    Container image for the judge job.

    Same chicken-and-egg as api_image, and the same resolution. The placeholder is never
    actually executed: the job only starts when there is a message on the queue, and
    nothing can enqueue one until the real API is deployed, which happens in the same
    cd-deploy run that replaces this value.
  EOT
  type        = string
  default     = "mcr.microsoft.com/dotnet/samples:aspnetapp"
}

variable "key_vault_secrets_officer_object_ids" {
  description = <<-EOT
    Object ids of every principal that runs `terraform apply` and therefore needs
    data-plane write access to the vault.

    Owner on the subscription does not imply Key Vault data-plane access when the vault
    uses RBAC authorization, which is why this grant has to exist at all. See the comment
    on azurerm_role_assignment.deployer_secrets_officer for why it is an explicit list
    rather than data.azurerm_client_config.current.object_id.

    Look either up with:
      az ad signed-in-user show --query id -o tsv
      az ad sp show --id <cicd-app-client-id> --query id -o tsv
  EOT
  type        = set(string)
  default = [
    # Human operator, for local applies.
    "7ccec34f-71b6-4d4b-bad9-bee536bdaf25",

    # codejudge-cicd service principal, for applies from cd-infra.
    "61046f3b-e29b-4da0-b7fe-f208aa979882",
  ]
}

variable "neon_connection_string" {
  description = <<-EOT
    Npgsql connection string for the Neon database.

    Required. The container app injects this into ConnectionStrings__CodeJudge as a Key
    Vault secret reference, so an empty value would deploy an API that falls back to
    localhost Postgres and fails on its first query while still passing /health.
  EOT
  type        = string
  sensitive   = true

  validation {
    condition     = trimspace(var.neon_connection_string) != ""
    error_message = "Set neon_connection_string (CI passes it from the NEON_CONNECTION_STRING secret). Without it the deployed API has no database."
  }
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
