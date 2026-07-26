output "environment_name" {
  description = "Container Apps environment name"
  value       = azurerm_container_app_environment.main.name
}

output "environment_id" {
  description = "Container Apps environment resource ID"
  value       = azurerm_container_app_environment.main.id
}

output "environment_default_domain" {
  description = "Container Apps environment generated default domain"
  value       = azurerm_container_app_environment.main.default_domain
}

output "api_name" {
  description = "API Container App name"
  value       = azurerm_container_app.api.name
}

output "api_id" {
  description = "API Container App resource ID"
  value       = azurerm_container_app.api.id
}

output "api_fqdn" {
  description = "API Container App generated FQDN"
  value       = azurerm_container_app.api.ingress[0].fqdn
}

output "web_name" {
  description = "Web Container App name"
  value       = azurerm_container_app.web.name
}

output "web_id" {
  description = "Web Container App resource ID"
  value       = azurerm_container_app.web.id
}

output "web_fqdn" {
  description = "Web Container App generated FQDN"
  value       = azurerm_container_app.web.ingress[0].fqdn
}

output "migration_job_name" {
  description = "Migration Container App job name"
  value       = azurerm_container_app_job.migrate.name
}

output "migration_job_id" {
  description = "Migration Container App job resource ID"
  value       = azurerm_container_app_job.migrate.id
}

output "observability_secret_configured" {
  description = "Whether the optional New Relic API key reference is configured"
  value       = var.new_relic_api_key_secret_name != null
}

output "outbound_ip_addresses" {
  description = "Complete advertised outbound IPv4 union for database firewall reconciliation"
  value = setunion(
    toset(azurerm_container_app.api.outbound_ip_addresses),
    toset(azurerm_container_app.web.outbound_ip_addresses),
    toset(azurerm_container_app_job.migrate.outbound_ip_addresses),
  )
}
