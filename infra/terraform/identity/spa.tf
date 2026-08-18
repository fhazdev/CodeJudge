# The SPA registration: a public client, because a browser app cannot keep a secret.

resource "azuread_application" "spa" {
  display_name     = "${var.name_prefix}-web"
  sign_in_audience = "AzureADandPersonalMicrosoftAccount"

  # Required, not optional: Entra rejects any registration that allows personal accounts
  # unless it issues v2 tokens. The SPA exposes no scopes of its own, so this block exists
  # solely to satisfy that constraint.
  api {
    requested_access_token_version = 2
  }

  # This block, and not `web`, is what makes the flow work. Registering the redirect URIs
  # under `web` instead leaves the token endpoint refusing the browser's origin with a
  # CORS error, which is the single most common way this gets misconfigured.
  single_page_application {
    redirect_uris = var.spa_redirect_uris
  }

  required_resource_access {
    resource_app_id = azuread_application.api.client_id

    resource_access {
      id   = local.access_as_user_scope_id
      type = "Scope"
    }
  }

  required_resource_access {
    resource_app_id = local.microsoft_graph_app_id

    resource_access {
      id   = local.graph_user_read_scope_id
      type = "Scope"
    }
  }
}

resource "azuread_service_principal" "spa" {
  client_id = azuread_application.spa.client_id
}

# Pre-authorization: the SPA is our own front end, so consenting to sign-in should not
# also prompt separately for the API scope. Without this the user sees two consent
# screens for what is, to them, one application.
resource "azuread_application_pre_authorized" "spa_on_api" {
  application_id       = azuread_application.api.id
  authorized_client_id = azuread_application.spa.client_id
  permission_ids       = [local.access_as_user_scope_id]
}
