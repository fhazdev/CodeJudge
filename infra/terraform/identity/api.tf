# The API registration: exposes the scope that the SPA asks for and the API validates.

locals {
  # Fixed rather than generated. The scope id ends up baked into the SPA's requested
  # permissions, and a value that churns on every apply would silently invalidate consent.
  access_as_user_scope_id = "9f3d2a1c-4b5e-4c6d-8a7b-1e2f3a4b5c6d"

  # Well-known Microsoft Graph ids.
  microsoft_graph_app_id   = "00000003-0000-0000-c000-000000000000"
  graph_user_read_scope_id = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"
}

resource "azuread_application" "api" {
  display_name = "${var.name_prefix}-api"

  # The decision that makes the live demo work for anyone. The default is single-tenant,
  # which would lock out every interviewer who is not in this directory.
  sign_in_audience = "AzureADandPersonalMicrosoftAccount"

  api {
    requested_access_token_version = 2

    oauth2_permission_scope {
      id    = local.access_as_user_scope_id
      value = "access_as_user"
      type  = "User"

      admin_consent_display_name = "Access CodeJudge as the signed-in user"
      admin_consent_description  = "Allows the app to call the CodeJudge API on behalf of the signed-in user."
      user_consent_display_name  = "Access CodeJudge on your behalf"
      user_consent_description   = "Allows CodeJudge to read problems and submit your solutions on your behalf."

      enabled = true
    }
  }

  # type = "User" above is load-bearing: a user-consentable scope means an interviewer in
  # a permissive tenant approves it themselves. Admin-only would require a tenant
  # administrator for every single visitor.

  required_resource_access {
    resource_app_id = local.microsoft_graph_app_id

    resource_access {
      id   = local.graph_user_read_scope_id
      type = "Scope"
    }
  }

  lifecycle {
    # azuread_application_identifier_uri below owns this attribute. Without ignoring it
    # here, the two resources fight: this one reads the URI the other one set as drift
    # and removes it on every apply, which silently breaks both the audience the API
    # validates and the scope the SPA requests.
    ignore_changes = [identifier_uris]
  }
}

# Separate resource because the URI contains the client id, which does not exist until
# after the application is created.
resource "azuread_application_identifier_uri" "api" {
  application_id = azuread_application.api.id
  identifier_uri = "api://${azuread_application.api.client_id}"
}

resource "azuread_service_principal" "api" {
  client_id = azuread_application.api.client_id
}
