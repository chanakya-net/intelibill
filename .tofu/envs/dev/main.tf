# Phase 10 grows this layer into Container Apps, Key Vault, and Log Analytics.
# Created early because Phase 7.3 registers database principals named after
# these identities: if they were created later their principal IDs would change
# and the SQL grants would silently stop matching.
data "azurerm_resource_group" "main" {
  name = var.resource_group_name
}

module "workload_identities" {
  source = "../../modules/workload-identities"

  env                 = "dev"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = var.location
}
