terraform {
  required_version = ">= 1.9"

  required_providers {
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
  }

  # State is local for now, deliberately.
  #
  # The azurerm backend needs a storage account, which does not exist until the platform
  # module is bootstrapped. Migrating to it later is a single `terraform init
  # -migrate-state` once infra/bootstrap has run, so nothing here needs to be recreated.
  #
  # backend "azurerm" {
  #   resource_group_name  = "rg-codejudge-tfstate"
  #   storage_account_name = "stcodejudgetfstate"
  #   container_name       = "tfstate"
  #   key                  = "identity.tfstate"
  # }
}

provider "azuread" {
  tenant_id = var.tenant_id
}
