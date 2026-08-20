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

  # Created by infra/bootstrap/bootstrap.ps1, which printed exactly this block.
  #
  # The resource group, storage account and container are shared by every project in the
  # subscription. Only the key is project-specific, which is what keeps a second project
  # from needing its own state account.
  #
  # CI passes the first three again as -backend-config flags in cd-infra.yml, from the
  # TFSTATE_* repository variables. They agree with these values; the flags exist so the
  # workflow does not depend on this file being correct for someone else's subscription.
  backend "azurerm" {
    resource_group_name  = "rg-tfstate"
    storage_account_name = "sttfstate8d1070c5"
    container_name       = "tfstate"
    key                  = "codejudge-platform.tfstate"
  }
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
