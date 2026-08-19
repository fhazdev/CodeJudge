# ---------------------------------------------------------------------------
# Container Apps
# ---------------------------------------------------------------------------

resource "azurerm_container_app_environment" "main" {
  name                       = "cae-${local.prefix}"
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  tags                       = local.tags
}

resource "azurerm_container_app" "api" {
  name                         = "ca-${local.prefix}-api"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = local.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.workload.id]
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # Zero, so an idle demo costs nothing. The cost is a 10 to 30 second cold start on
    # the first request after a quiet period, which the SPA is written to explain rather
    # than appear broken during. Set to 1 for a demo day if that trade stops being worth it.
    min_replicas = 0
    max_replicas = 2

    container {
      name   = "api"
      image  = var.api_image
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      env {
        name  = "ASPNETCORE_HTTP_PORTS"
        value = "8080"
      }

      env {
        name  = "AzureAd__ClientId"
        value = var.entra_api_client_id
      }

      env {
        name  = "AzureAd__Audience"
        value = "api://${var.entra_api_client_id}"
      }

      env {
        name  = "AzureAd__TenantId"
        value = "common"
      }

      env {
        name  = "Cors__AllowedOrigins__0"
        value = var.spa_allowed_origin != "" ? var.spa_allowed_origin : "https://${azurerm_static_web_app.web.default_host_name}"
      }

      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.workload.client_id
      }

      env {
        name  = "CODEJUDGE_QUEUE_URI"
        value = "https://${azurerm_storage_account.main.name}.queue.core.windows.net/${azurerm_storage_queue.submissions.name}"
      }

      env {
        name  = "CODEJUDGE_KEYVAULT_URI"
        value = azurerm_key_vault.main.vault_uri
      }

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health"
      }

      readiness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health"
      }
    }
  }

  lifecycle {
    # CI owns the image tag from the first deploy onward. Without this, every
    # `terraform apply` would roll the app back to whatever tag the variable happens to
    # hold, silently undoing the most recent deployment.
    ignore_changes = [template[0].container[0].image]
  }
}

# ---------------------------------------------------------------------------
# Static Web App
# ---------------------------------------------------------------------------

resource "azurerm_static_web_app" "web" {
  name                = "stapp-${local.prefix}"
  resource_group_name = azurerm_resource_group.main.name

  # Static Web Apps is not available in every region; these are the free-tier ones.
  location = "eastus2"

  sku_tier = "Free"
  sku_size = "Free"

  tags = local.tags
}

# The Container Apps Job for the judge arrives in phase 2, once the worker actually has
# a dequeue loop to run. Its queue and its identity already exist above.
