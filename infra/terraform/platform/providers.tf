terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Populated by infra/bootstrap/bootstrap.ps1, which prints the exact block to paste.
  # Left commented so `terraform init` works locally before the bootstrap has been run.
  #
  # backend "azurerm" {
  #   resource_group_name  = "rg-codejudge-tfstate"
  #   storage_account_name = "stcjtfstate________"
  #   container_name       = "tfstate"
  #   key                  = "platform.tfstate"
  # }
}

provider "azurerm" {
  features {
    key_vault {
      # Soft delete is not optional on Key Vault, and the default 90-day retention means
      # a destroyed vault blocks recreating one with the same name for three months.
      # Purging on destroy keeps a demo environment re-creatable.
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = true
    }
  }

  subscription_id = var.subscription_id
}
