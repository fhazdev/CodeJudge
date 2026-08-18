output "api_client_id" {
  description = "Audience the API validates tokens against."
  value       = azuread_application.api.client_id
}

output "api_identifier_uri" {
  description = "api://<client-id>. Scopes are requested as <this>/access_as_user."
  value       = azuread_application_identifier_uri.api.identifier_uri
}

output "api_scope" {
  description = "The full scope string the SPA requests."
  value       = "${azuread_application_identifier_uri.api.identifier_uri}/access_as_user"
}

output "spa_client_id" {
  description = "Client id for MSAL.js in the React app."
  value       = azuread_application.spa.client_id
}

output "tenant_id" {
  value = var.tenant_id
}

output "authority" {
  description = "/common, so both work accounts from any tenant and personal accounts can sign in."
  value       = "https://login.microsoftonline.com/common"
}

output "cicd_client_id" {
  description = "Null until var.github_repository is set."
  value       = var.github_repository == "" ? null : azuread_application.cicd[0].client_id
}
