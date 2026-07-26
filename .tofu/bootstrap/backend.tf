terraform {
  backend "azurerm" {
    resource_group_name  = "intelibill-shared"
    storage_account_name = "intelibilltfstate01"
    container_name       = "tfstate"
    key                  = "bootstrap.tfstate"
    use_azuread_auth     = true
  }
}