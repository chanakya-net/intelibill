output "identities" {
  description = "Runtime and migrator identities for Phase 7.3 and Phase 10"
  value       = module.workload_identities.identities
}

output "key_vault" {
  description = "Vault name and base URI for Phase 9 entry and Phase 10 references"
  value = {
    name               = module.key_vault.name
    id                 = module.key_vault.id
    vault_uri          = module.key_vault.vault_uri
    jwt_signing_key_id = module.key_vault.jwt_signing_key_id
  }
}

output "container_apps" {
  description = "Environment workload resources and outbound database firewall inputs"
  value = {
    environment = {
      name           = module.environment_infrastructure.environment_name
      id             = module.environment_infrastructure.environment_id
      default_domain = module.environment_infrastructure.environment_default_domain
    }
    api = {
      name = module.environment_infrastructure.api_name
      id   = module.environment_infrastructure.api_id
      fqdn = module.environment_infrastructure.api_fqdn
    }
    web = {
      name = module.environment_infrastructure.web_name
      id   = module.environment_infrastructure.web_id
      fqdn = module.environment_infrastructure.web_fqdn
    }
    migration_job = {
      name = module.environment_infrastructure.migration_job_name
      id   = module.environment_infrastructure.migration_job_id
    }
    outbound_ip_addresses           = module.environment_infrastructure.outbound_ip_addresses
    observability_secret_configured = module.environment_infrastructure.observability_secret_configured
  }
}
