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

      # Resolved by Container Apps from Key Vault at revision start, not by the app.
      # Program.cs reads GetConnectionString("CodeJudge") first, and the double
      # underscore is how .NET configuration spells the "ConnectionStrings:CodeJudge"
      # key in an environment variable. Without this the API silently falls back to
      # DesignTimeDbContextFactory.LocalConnectionString, which points at localhost.
      env {
        name        = "ConnectionStrings__CodeJudge"
        secret_name = "neon-connection-string"
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

  # Key Vault reference rather than a literal. The value never enters Terraform state
  # as container app configuration, and `versionless_id` means rotating the secret in
  # Key Vault does not require an apply to take effect on the next revision.
  #
  # Reading it needs the Key Vault Secrets User grant on the workload identity, which
  # `workload_secrets_user` in main.tf already carries.
  secret {
    name                = "neon-connection-string"
    key_vault_secret_id = azurerm_key_vault_secret.neon_connection_string.versionless_id
    identity            = azurerm_user_assigned_identity.workload.id
  }

  lifecycle {
    # CI owns the image tag from the first deploy onward. Without this, every
    # `terraform apply` would roll the app back to whatever tag the variable happens to
    # hold, silently undoing the most recent deployment.
    ignore_changes = [template[0].container[0].image]
  }
}

# ---------------------------------------------------------------------------
# Container Apps Job: the judge
# ---------------------------------------------------------------------------

# Event-driven, not a long-lived worker. One queue message is one unit of work: KEDA
# sees queue depth, starts a container, that container claims exactly one message, judges
# it, writes the verdict and exits. `worker --once` in the image is precisely that shape,
# which is why the judge core was built around ProcessNextAsync rather than a dequeue
# loop: the local and deployed shapes stay identical in the component that is hardest to
# debug remotely.
resource "azurerm_container_app_job" "judge" {
  name                         = "caj-${local.prefix}-judge"
  location                     = azurerm_resource_group.main.location
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  tags                         = local.tags

  # Layer 2 of the five-layer timeout budget. Strictly larger than the parent's own 90 s
  # submission budget on purpose: when this fires the execution is killed with no verdict
  # written and the submission sits in Running until the message redelivers, so it is the
  # backstop for a hung parent and never the thing that should fire in normal operation.
  replica_timeout_in_seconds = 300

  # A failed execution leaves its message invisible for the full 600 s visibility timeout
  # set in SubmissionQueueReader, so a retry inside that window finds no work and exits
  # cleanly. One retry absorbs a transient startup failure without risking a double judge.
  replica_retry_limit = 1

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.workload.id]
  }

  secret {
    name                = "neon-connection-string"
    key_vault_secret_id = azurerm_key_vault_secret.neon_connection_string.versionless_id
    identity            = azurerm_user_assigned_identity.workload.id
  }

  event_trigger_config {
    # One message, one execution. replica_completion_count matches parallelism so an
    # execution counts as finished the moment its single replica exits.
    parallelism              = 1
    replica_completion_count = 1

    scale {
      min_executions = 0

      # Concurrency ceiling, not a target. Each execution burns the Container Apps free
      # grant only while it runs, and five parallel judges is far more than a demo needs.
      max_executions = 5

      # KEDA polls this often, so it is the floor on how long a submission waits before
      # judging even begins. The SPA gives up polling at 120 s and a cold start already
      # spends 10 to 30 s of that, which makes the 30 s default uncomfortably tight.
      # Queue transactions are far too cheap for 10 s to register on the bill.
      polling_interval_in_seconds = 10

      rules {
        name             = "queue-depth"
        custom_rule_type = "azure-queue"

        # Managed identity rather than a connection-string secret. The alternative is
        # storing the storage account key as a job secret, which is exactly what the
        # workload identity exists to avoid. Needs the Reader grant in main.tf: queue
        # *length* is a management-plane read, separate from reading messages.
        identity_id = azurerm_user_assigned_identity.workload.id

        metadata = {
          accountName = azurerm_storage_account.main.name
          queueName   = azurerm_storage_queue.submissions.name

          # Target depth per execution. One means one container per queued submission.
          queueLength = "1"
        }
      }
    }
  }

  template {
    container {
      name = "judge"

      image = var.judge_image

      # The value section 5 of the build plan commits to. The parent holds Roslyn and EF
      # Core while the child gets a 256 MB GC heap limit plus 128 MB of headroom, so this
      # has to cover both processes at once.
      cpu    = 0.5
      memory = "1Gi"

      # Note the name: the judge reads this variable directly via
      # Environment.GetEnvironmentVariable, rather than through .NET configuration the way
      # the API reads ConnectionStrings__CodeJudge. Same Key Vault secret, different key,
      # because they are two different processes reading it two different ways.
      env {
        name        = "CODEJUDGE_CONNECTION"
        secret_name = "neon-connection-string"
      }

      # QueueUri wins over QueueClientFactory's connection-string path, which is what
      # selects managed identity instead of Azurite's development storage default.
      env {
        name  = "CODEJUDGE_QUEUE_URI"
        value = "https://${azurerm_storage_account.main.name}.queue.core.windows.net/${azurerm_storage_queue.submissions.name}"
      }

      env {
        name  = "CODEJUDGE_QUEUE_NAME"
        value = azurerm_storage_queue.submissions.name
      }

      # Tells DefaultAzureCredential which identity to use. The job has exactly one
      # assigned, but the credential will not guess.
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.workload.client_id
      }
    }
  }

  lifecycle {
    # CI owns the image tag from the first deploy onward, same as the API.
    ignore_changes = [template[0].container[0].image]
  }
}

# ---------------------------------------------------------------------------
# Static Web App
# ---------------------------------------------------------------------------

resource "azurerm_static_web_app" "web" {
  name                = "stapp-${local.prefix}"
  resource_group_name = azurerm_resource_group.main.name

  # Static Web Apps exists in only five regions: Central US, East US 2, West US 2,
  # West Europe and East Asia. Kept separate from var.location because the rest of the
  # stack can move anywhere while this cannot, so pinning it here fails loudly at plan
  # time rather than at apply time on an unsupported region.
  location = "centralus"

  sku_tier = "Free"
  sku_size = "Free"

  tags = local.tags
}

# The Container Apps Job for the judge arrives in phase 2, once the worker actually has
# a dequeue loop to run. Its queue and its identity already exist above.
