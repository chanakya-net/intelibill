terraform {
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id

  # The state account disables shared keys, so every data-plane call
  # (blob availability poll, container ops) must use Entra ID instead.
  storage_use_azuread = true
}

resource "azurerm_resource_group" "shared" {
  name     = var.resource_group_name
  location = var.location
}

resource "azurerm_storage_account" "tfstate" {
  name                     = var.state_storage_account_name
  resource_group_name      = azurerm_resource_group.shared.name
  location                 = azurerm_resource_group.shared.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  # State files are the crown jewels: they describe every resource you own.
  shared_access_key_enabled       = false
  allow_nested_items_to_be_public = false

  blob_properties {
    versioning_enabled = true
    delete_retention_policy { days = 30 }
  }
}

resource "azurerm_storage_container" "tfstate" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.tfstate.id
  container_access_type = "private"
}

# One container per state layer. Azure RBAC cannot scope blob data access below a
# container, so a single shared container would let the dev apply identity rewrite
# prod state. Bootstrap state stays in the container above.
resource "azurerm_storage_container" "state" {
  for_each              = local.state_layers
  name                  = "tfstate-${each.key}"
  storage_account_id    = azurerm_storage_account.tfstate.id
  container_access_type = "private"
}