# Phase 10 grows this layer into Container Apps, Key Vault, and Log Analytics.
# Created early because Phase 7.3 registers database principals named after
# these identities: if they were created later their principal IDs would change
# and the SQL grants would silently stop matching.
data "azurerm_resource_group" "main" {
  name = var.resource_group_name
}

module "workload_identities" {
  source = "../../modules/workload-identities"

  env                 = "prod"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = var.location
}

data "azurerm_client_config" "current" {}

# 10A foundation, applied ahead of Phase 9: the vault has to exist before there
# is anywhere to put a secret. Container Apps, Log Analytics, and the workloads
# that reference these secrets arrive in 10B.
module "key_vault" {
  source = "../../modules/key-vault"

  env                 = "prod"
  resource_group_name = data.azurerm_resource_group.main.name
  location            = var.location
  tenant_id           = data.azurerm_client_config.current.tenant_id
  app_principal_id    = module.workload_identities.identities.app.principal_id

  secret_officer_object_ids = var.secret_officer_object_ids

  soft_delete_retention_days = 90
  purge_protection_enabled   = true
}
