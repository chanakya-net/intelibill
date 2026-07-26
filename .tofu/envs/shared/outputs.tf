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

output "dns_zone_name" {
  description = "DNS zone name, or null when DNS is not managed here"
  value       = var.domain_name == null ? null : module.dns[0].zone_name
}

output "dns_name_servers" {
  description = "Nameservers for the Phase 6 registrar change, or null"
  value       = var.domain_name == null ? null : module.dns[0].name_servers
}
