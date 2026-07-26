terraform {
  required_version = ">= 1.8"

  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }

  # Backends cannot take variables. This layer's apply identity holds blob data
  # Contributor on tfstate-shared and nothing else, so a wrong container here
  # fails closed rather than corrupting another layer.
  backend "azurerm" {
    resource_group_name  = "intelibill-shared"
    storage_account_name = "intelibilltfstate01"
    container_name       = "tfstate-shared"
    key                  = "shared.tfstate"
    use_azuread_auth     = true
  }
}

provider "azurerm" {
  features {}
  subscription_id     = var.subscription_id
  storage_use_azuread = true
}
