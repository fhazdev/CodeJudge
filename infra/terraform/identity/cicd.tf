# The GitHub Actions registration.
#
# Not created until var.github_repository is set. Federated credentials are scoped to a
# specific repository and ref, and pointing one at a repository that does not exist yet
# would be worse than having none: it looks configured while granting nothing.
#
# Note there is no client secret anywhere here. OIDC federation means GitHub presents a
# short-lived token that Entra trusts because of the subject match below, so there is no
# long-lived credential to leak or rotate.

resource "azuread_application" "cicd" {
  count = var.github_repository == "" ? 0 : 1

  display_name     = "${var.name_prefix}-cicd"
  sign_in_audience = "AzureADMyOrg"
}

resource "azuread_service_principal" "cicd" {
  count = var.github_repository == "" ? 0 : 1

  client_id = azuread_application.cicd[0].client_id
}

resource "azuread_application_federated_identity_credential" "main_branch" {
  count = var.github_repository == "" ? 0 : 1

  application_id = azuread_application.cicd[0].id
  display_name   = "github-main"
  description    = "Pushes to the default branch, for terraform apply and deploys."
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://token.actions.githubusercontent.com"
  subject        = "${var.github_subject_prefix}:ref:refs/heads/main"
}

resource "azuread_application_federated_identity_credential" "pull_request" {
  count = var.github_repository == "" ? 0 : 1

  application_id = azuread_application.cicd[0].id
  display_name   = "github-pull-request"
  description    = "Pull requests, for terraform plan only."
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://token.actions.githubusercontent.com"
  subject        = "${var.github_subject_prefix}:pull_request"
}
