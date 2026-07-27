terraform {
  required_version = ">= 1.8"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }
}

# One server, one database per environment. Cheaper than a server per
# environment, at the cost of making Phase 7.4's grants the only thing standing
# between dev and production data — verify them with 7.5, every time they change.
resource "azurerm_postgresql_flexible_server" "main" {
  name                = "intelibill-pg-${var.name_serial}"
  resource_group_name = var.resource_group_name
  location            = var.location
  version             = var.postgres_version

  # Controlled-launch default. Resize after measuring CPU credits, connection
  # usage and latency; do not use this as an HA promise. Both environments share
  # this compute, so dev load spends production's burst credits.
  sku_name   = var.postgres_sku
  storage_mb = var.storage_mb

  backup_retention_days        = var.backup_retention_days
  geo_redundant_backup_enabled = false

  # Public-network launch exception: reachability is controlled entirely by the
  # firewall rules below, and there is no password to brute-force.
  public_network_access_enabled = true

  authentication {
    active_directory_auth_enabled = true
    password_auth_enabled         = false
    tenant_id                     = var.tenant_id
  }

  lifecycle {
    prevent_destroy = true

    # Storage auto-grows and Azure picks the availability zone. Neither should
    # read as drift on the layer that owns your data.
    ignore_changes = [storage_mb, zone]
  }
}

# Phase 7.1. One administrator for one server, covering both databases.
# Managed through IaC rather than by hand so that it survives a rebuild — losing
# the only administrator on a password-less server locks everyone out.
resource "azurerm_postgresql_flexible_server_active_directory_administrator" "admin" {
  server_name         = azurerm_postgresql_flexible_server.main.name
  resource_group_name = var.resource_group_name
  tenant_id           = var.tenant_id
  object_id           = var.admin_object_id
  principal_name      = var.admin_principal_name
  principal_type      = "User"
}

resource "azurerm_postgresql_flexible_server_database" "app" {
  for_each  = var.databases
  name      = each.value
  server_id = azurerm_postgresql_flexible_server.main.id
  collation = "en_US.utf8"
  charset   = "utf8"

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "allowed" {
  for_each         = var.allowed_ip_rules
  name             = each.key
  server_id        = azurerm_postgresql_flexible_server.main.id
  start_ip_address = each.value.start_ip
  end_ip_address   = each.value.end_ip
}

# On a public server these are not optional hardening; they are what keeps the
# exception defensible.
resource "azurerm_postgresql_flexible_server_configuration" "require_tls" {
  name      = "require_secure_transport"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "ON"
}

resource "azurerm_postgresql_flexible_server_configuration" "min_tls" {
  name      = "ssl_min_protocol_version"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "TLSv1.2"
}

# Throttles repeated failed authentication from a single host — the main
# remaining nuisance once passwords are disabled.
resource "azurerm_postgresql_flexible_server_configuration" "connection_throttle" {
  name      = "connection_throttle.enable"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "on"
}

resource "azurerm_postgresql_flexible_server_configuration" "log_connections" {
  name      = "log_connections"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "on"
}

resource "azurerm_postgresql_flexible_server_configuration" "log_disconnections" {
  name      = "log_disconnections"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "on"
}
