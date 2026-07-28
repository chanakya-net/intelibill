# Phase 10 attaches the existing workload identities to Container Apps without
# replacing them. Phase 7.3 registered database principals against their object
# IDs, so replacement would silently detach every SQL grant.
locals {
  env           = "dev"
  database_name = "intelibill_dev"
}

data "azurerm_resource_group" "main" {
  name = var.resource_group_name
}

data "azurerm_log_analytics_workspace" "shared" {
  name                = "intelibill-logs"
  resource_group_name = data.azurerm_resource_group.main.name
}

data "azurerm_user_assigned_identity" "deploy" {
  name                = "id-gha-deploy-${local.env}"
  resource_group_name = data.azurerm_resource_group.main.name
}

data "azurerm_subscription" "current" {}

data "azurerm_role_definition" "container_app_deployer" {
  name  = "Intelibill Container App Deployer"
  scope = data.azurerm_subscription.current.id
}

module "workload_identities" {
  source = "../../modules/workload-identities"

  env                 = local.env
  resource_group_name = data.azurerm_resource_group.main.name
  location            = var.location
}

data "azurerm_client_config" "current" {}

# The Phase 9 Key Vault remains owned by this environment state. Phase 10
# workloads consume its IDs and versionless references without reading values.
module "key_vault" {
  source = "../../modules/key-vault"

  env                 = local.env
  resource_group_name = data.azurerm_resource_group.main.name
  location            = var.location
  tenant_id           = data.azurerm_client_config.current.tenant_id
  app_principal_id    = module.workload_identities.identities.app.principal_id

  secret_officer_object_ids = var.secret_officer_object_ids

  soft_delete_retention_days = 7
  purge_protection_enabled   = false
}

module "environment_infrastructure" {
  source = "../../modules/environment-infrastructure"

  env                       = local.env
  resource_group_name       = data.azurerm_resource_group.main.name
  location                  = var.location
  log_analytics_id          = data.azurerm_log_analytics_workspace.shared.id
  deploy_principal_id       = data.azurerm_user_assigned_identity.deploy.principal_id
  deploy_role_definition_id = data.azurerm_role_definition.container_app_deployer.id
  app_identity              = module.workload_identities.identities.app
  migrator_identity         = module.workload_identities.identities.migrator
  key_vault                 = module.key_vault
  database = {
    host          = "intelibill-pg-01.postgres.database.azure.com"
    port          = 5432
    name          = local.database_name
    max_pool_size = 12
  }
  new_relic_otlp_endpoint       = "https://otlp.eu01.nr-data.net:4318"
  new_relic_api_key_secret_name = var.new_relic_api_key_secret_name
  bootstrap_image               = var.bootstrap_image
}
