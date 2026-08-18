variable "tenant_id" {
  description = "Entra tenant that owns the app registrations. Not a secret."
  type        = string
  default     = "5ed810e6-3688-43e5-b457-92a318ce1248"
}

variable "name_prefix" {
  description = "Prefix for app registration display names."
  type        = string
  default     = "codejudge"
}

variable "spa_redirect_uris" {
  description = <<-EOT
    Redirect URIs for the SPA registration.

    localhost must be present or you cannot develop. The deployed Static Web App
    hostname is added once the platform module has created it.

    Note the trailing slash: Entra rejects a root redirect URI without one. MSAL's
    redirectUri must be set to the identical string, so the SPA config spells it out
    rather than relying on window.location.origin, which has no trailing slash.
  EOT
  type        = list(string)
  default = [
    "http://localhost:5173/"
  ]
}

variable "github_repository" {
  description = <<-EOT
    GitHub repository in "owner/name" form, used as the subject of the CI/CD
    federated credentials.

    Empty means the CI/CD registration is not created at all. There is no remote yet,
    and a federated credential pointing at a repository that does not exist would be
    worse than absent.
  EOT
  type        = string
  default     = ""
}
