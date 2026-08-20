locals {
  prefix = "${var.name_prefix}-${var.environment}"

  # Storage account names: globally unique, 3-24 chars, lowercase alphanumeric only.
  # No hyphens, which is why this one is built differently from every other name here.
  storage_account_name = substr(
    lower(replace("st${var.name_prefix}${var.environment}${random_string.suffix.result}", "-", "")),
    0, 24
  )

  key_vault_name = substr("kv-${var.name_prefix}-${random_string.suffix.result}", 0, 24)

  tags = {
    project     = "codejudge"
    environment = var.environment
    managed_by  = "terraform"
  }
}

# Key Vault and storage account names are globally unique. A random suffix avoids a
# collision with someone else's resource, which would otherwise fail the apply with a
# message that does not obviously mean "this name is taken".
resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

resource "azurerm_resource_group" "main" {
  name     = "rg-${local.prefix}"
  location = var.location
  tags     = local.tags
}

# ---------------------------------------------------------------------------
# Observability
# ---------------------------------------------------------------------------

# Required by the Container Apps environment whether or not we query it. Also the only
# line item on this project likely to appear on a bill, if logging gets generous.
resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.prefix}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = local.tags
}

# ---------------------------------------------------------------------------
# Identity
# ---------------------------------------------------------------------------

# One user-assigned identity shared by the API and, from phase 2, the judge job.
# User-assigned rather than system-assigned so role assignments survive the container
# app being destroyed and recreated, which otherwise means re-granting on every rebuild.
resource "azurerm_user_assigned_identity" "workload" {
  name                = "id-${local.prefix}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.tags
}

# ---------------------------------------------------------------------------
# Storage and the submission queue
# ---------------------------------------------------------------------------

resource "azurerm_storage_account" "main" {
  name                     = local.storage_account_name
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  # Nothing here is ever served publicly. Submissions are queue messages, not blobs.
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = true

  tags = local.tags
}

resource "azurerm_storage_queue" "submissions" {
  name               = "submissions"
  storage_account_id = azurerm_storage_account.main.id
}

# The API writes messages; the judge job reads and deletes them. Both run as the same
# identity in v1, so one assignment covers both. Splitting them into sender-only and
# processor-only identities is a phase 4 hardening item.
resource "azurerm_role_assignment" "workload_queue_data" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = azurerm_user_assigned_identity.workload.principal_id
}

# The KEDA scaler reads queue *length* rather than messages, which is a management-plane
# read and needs its own grant. Without this the job silently never triggers.
resource "azurerm_role_assignment" "workload_storage_reader" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.workload.principal_id
}

# ---------------------------------------------------------------------------
# Key Vault
# ---------------------------------------------------------------------------

data "azurerm_client_config" "current" {}

resource "azurerm_key_vault" "main" {
  name                = local.key_vault_name
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  # RBAC rather than the legacy access policy model: one authorization system for the
  # whole subscription instead of two, and it composes with the managed identity above.
  rbac_authorization_enabled = true

  purge_protection_enabled   = false
  soft_delete_retention_days = 7

  tags = local.tags
}

# Whoever runs the apply needs to be able to write the secret they are creating.
resource "azurerm_role_assignment" "deployer_secrets_officer" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "workload_secrets_user" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.workload.principal_id
}

resource "azurerm_key_vault_secret" "neon_connection_string" {
  name         = "neon-connection-string"
  value        = var.neon_connection_string
  key_vault_id = azurerm_key_vault.main.id

  depends_on = [azurerm_role_assignment.deployer_secrets_officer]
}
