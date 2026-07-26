# Consumed by the environment layers in Phase 10 and by Phase 7's bootstrap
# commands. Names and hostnames only — never credentials, because there are none.

output "postgres" {
  description = "The shared server and its databases"
  value = {
    server_name = module.database.server_name
    fqdn        = module.database.fqdn
    databases   = module.database.database_names
  }
}

output "postgres_server_id" {
  description = "Server resource ID, for diagnostic settings and role assignments"
  value       = module.database.server_id
}

output "container_apps_firewall_ips" {
  description = "Current Container Apps outbound addresses read from dev and prod state"
  value       = local.container_apps_firewall_ips
}

output "retained_container_apps_outbound_ips" {
  description = "Previously advertised addresses retained for the current transition apply"
  value       = var.retained_container_apps_outbound_ips
}

output "log_analytics" {
  description = "Shared Azure Monitor destination; no workspace keys are exposed"
  value = {
    id           = module.shared_monitoring.workspace.id
    name         = module.shared_monitoring.workspace.name
    workspace_id = module.shared_monitoring.workspace.customer_id
  }
}

output "dns_zone_name" {
  description = "DNS zone name, or null when DNS is not managed here"
  value       = var.domain_name == null ? null : module.dns[0].zone_name
}

output "dns_name_servers" {
  description = "Nameservers for the Phase 6 registrar change, or null"
  value       = var.domain_name == null ? null : module.dns[0].name_servers
}
