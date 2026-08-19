output "resource_group_name" {
  value = azurerm_resource_group.main.name
}

output "api_url" {
  description = "Set this as VITE_API_BASE_URL for the deployed SPA."
  value       = "https://${azurerm_container_app.api.ingress[0].fqdn}"
}

output "api_container_app_name" {
  description = "Target for `az containerapp update` in cd-deploy."
  value       = azurerm_container_app.api.name
}

output "web_hostname" {
  description = "Add this, with an https:// prefix and a trailing slash, as a redirect URI on the SPA app registration."
  value       = azurerm_static_web_app.web.default_host_name
}

output "web_api_key" {
  description = "Deployment token for the Static Web Apps GitHub action."
  value       = azurerm_static_web_app.web.api_key
  sensitive   = true
}

output "storage_account_name" {
  value = azurerm_storage_account.main.name
}

output "submissions_queue_name" {
  value = azurerm_storage_queue.submissions.name
}

output "workload_identity_client_id" {
  description = "Set as AZURE_CLIENT_ID so DefaultAzureCredential picks the right identity."
  value       = azurerm_user_assigned_identity.workload.client_id
}

output "workload_identity_id" {
  description = "Resource id, needed by the phase 2 judge job."
  value       = azurerm_user_assigned_identity.workload.id
}

output "key_vault_uri" {
  value = azurerm_key_vault.main.vault_uri
}
