output "server_id" {
  description = "Resource ID, for role assignments and diagnostic settings"
  value       = azurerm_postgresql_flexible_server.main.id
}

output "server_name" {
  description = "Server name, for az postgres commands in Phase 7"
  value       = azurerm_postgresql_flexible_server.main.name
}

output "fqdn" {
  description = "Hostname every environment connects to — the same host for both"
  value       = azurerm_postgresql_flexible_server.main.fqdn
}

output "database_names" {
  description = "Databases created on the server"
  value       = [for d in azurerm_postgresql_flexible_server_database.app : d.name]
}
