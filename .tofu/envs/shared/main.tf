# The shared resource group is created in bootstrap. Read it rather than
# declaring it again — two layers owning one resource is how a `tofu destroy`
# in the wrong directory takes out the state account.
data "azurerm_resource_group" "shared" {
  name = var.resource_group_name
}

# One server hosting both environments as separate databases. Chosen for cost:
# roughly half the compute bill of a server per environment.
#
# The consequence is that dev and prod are no longer separated by topology, only
# by grants. Phase 7.4 (REVOKE CONNECT, per-database roles, default privileges)
# is the entire boundary, and Phase 7.5's isolation test is the only thing that
# proves it still holds. Re-run 7.5 after any grant change.
module "database" {
  source = "../../modules/database"

  resource_group_name   = data.azurerm_resource_group.shared.name
  location              = var.location
  tenant_id             = var.tenant_id
  admin_object_id       = var.admin_object_id
  admin_principal_name  = var.admin_principal_name
  databases             = var.databases
  postgres_sku          = var.postgres_sku
  storage_mb            = var.storage_mb
  backup_retention_days = var.backup_retention_days
  allowed_ip_rules      = var.allowed_ip_rules
}

module "shared_monitoring" {
  source = "../../modules/shared-monitoring"

  resource_group_name = data.azurerm_resource_group.shared.name
  location            = var.location
  postgres_server_id  = module.database.server_id
  daily_quota_gb      = var.log_analytics_daily_quota_gb
}

module "dns" {
  source = "../../modules/dns"
  count  = var.domain_name == null ? 0 : 1

  domain_name         = var.domain_name
  resource_group_name = data.azurerm_resource_group.shared.name
}

# No container registry resource: this repository publishes to public GHCR with
# the job-scoped GITHUB_TOKEN and Container Apps pulls the digest anonymously.
# Adding ACR later means a registry module, AcrPush on the deploy identities,
# and AcrPull on each app identity — see Phase 3.2 and 10.2.
