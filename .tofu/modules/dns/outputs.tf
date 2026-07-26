output "zone_name" {
  value = azurerm_dns_zone.main.name
}

output "zone_id" {
  value = azurerm_dns_zone.main.id
}

# Phase 6 repoints the registrar at these.
output "name_servers" {
  description = "Nameservers to set at the registrar"
  value       = azurerm_dns_zone.main.name_servers
}
